using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using ValheimHelper.Core.Storage;

namespace ValheimHelper
{
    /// <summary>
    /// 사람끼리 동시 열기 손님 쪽(상자 기능 ④ 다-3, decisions.txt "상자 기능 확정" ④): 우리 모드 손님은 건축물 상자를 VH_ChestOpen으로 연다(주인은 호스트 그대로).
    /// 화면의 옮기기(끌어 놓기·Shift 옮기기·나누기·모두 꺼내기·모두 쌓기)는 바닐라 그대로 손님 쪽 상자 사본에서 먼저 일어나고(예측 — 핑이 안 보임),
    /// 그 전후 상자 칸 차이(Core ChestDiff)를 한 부탁으로 호스트에 보낸다. 답(물건마다 상자가 실제로 변한 수)이 예측과 다르면 GuestLedger가 가방을 그만큼 고친다.
    /// 상자 칸 화면은 호스트 ZDO가 바뀔 때 다시 읽는다(주인 아닌 바닐라 Container.Load). 상자 물건을 땅에 버리기·바로 쓰기는 막는다(가방을 안 거쳐 셈이 안 맞음).
    /// 시험(Force)은 호스트 안에서 손님처럼: 상자 사본을 따로 두고(View) 부탁을 0.2~0.5초 늦춰 보낸다.
    /// </summary>
    public static class SharedGuest
    {
        /// <summary>시험: 호스트에서도 손님 길로 연다(가짜 손님).</summary>
        public static bool Force;
        /// <summary>시험: 부탁(열기·옮기기·닫기)을 이만큼 늦춰 보냄(초, 핑 흉내).</summary>
        public static float DelayMin, DelayMax;

        const float OpenTimeout = 3f;   // 이 안에 열림 답이 없으면 호스트에 모드가 없음 → 바닐라 열기
        const float ReplyTimeout = 15f; // 접속 끊김 정도 — 보낸 것은 돌려받고 꺼낸 것은 둠

        static Container s_container;
        static ZDOID s_id;
        static Inventory s_view;      // 손님 화면의 상자(진짜 손님 = 상자 자기 Inventory, 시험 = 따로 둔 사본)
        static bool s_confirmed;
        static float s_openedAt;
        static uint s_rev;

        static readonly GuestLedger s_ledger = new GuestLedger();
        /// <summary>부탁 번호 → 물건 키별 본(돌려받을 때 만들 물건, 넣기에 쓴 것).</summary>
        static readonly Dictionary<int, Dictionary<string, ItemDrop.ItemData>> s_templates = new Dictionary<int, Dictionary<string, ItemDrop.ItemData>>();

        sealed class Outgoing { public float Due; public string Rpc; public object[] Args; }
        static readonly List<Outgoing> s_outbox = new List<Outgoing>();
        static float s_lastDue;

        static readonly AccessTools.FieldRef<Container, Inventory> ContainerInv = AccessTools.FieldRefAccess<Container, Inventory>("m_inventory");
        static readonly AccessTools.FieldRef<InventoryGui, Container> GuiContainer = AccessTools.FieldRefAccess<InventoryGui, Container>("m_currentContainer");
        static readonly AccessTools.FieldRef<InventoryGui, InventoryGrid> GuiGrid = AccessTools.FieldRefAccess<InventoryGui, InventoryGrid>("m_containerGrid");
        static readonly AccessTools.FieldRef<InventoryGui, Animator> GuiAnimator = AccessTools.FieldRefAccess<InventoryGui, Animator>("m_animator");
        static readonly AccessTools.FieldRef<InventoryGui, ItemDrop.ItemData> GuiDragItem = AccessTools.FieldRefAccess<InventoryGui, ItemDrop.ItemData>("m_dragItem");
        static readonly AccessTools.FieldRef<InventoryGui, Inventory> GuiDragInv = AccessTools.FieldRefAccess<InventoryGui, Inventory>("m_dragInventory");
        static readonly AccessTools.FieldRef<InventoryGui, bool> GuiFirst = AccessTools.FieldRefAccess<InventoryGui, bool>("m_firstContainerUpdate");
        static readonly Action<InventoryGui> CloseContainer =
            AccessTools.MethodDelegate<Action<InventoryGui>>(AccessTools.Method(typeof(InventoryGui), "CloseContainer"));
        static readonly Func<Container, long, bool> CheckAccess =
            AccessTools.MethodDelegate<Func<Container, long, bool>>(AccessTools.Method(typeof(Container), "CheckAccess"));

        public static bool Active => s_container != null;
        public static bool Confirmed => s_confirmed;
        public static Inventory View => s_view;
        public static int Pending => s_ledger.Count;
        /// <summary>답 기다리며 가방에 꺼내 온 그 물건 수(다-3 나머지: 버리기·쓰기·제작 막기가 씀).</summary>
        public static int Locked(ItemDrop.ItemData item) => item == null ? 0 : s_ledger.Locked(SharedChests.Key(item));

        [HarmonyPatch(typeof(Game), "Start")]
        static class GameStart
        {
            static void Postfix()
            {
                Reset();
                SharedChests.OpenReplied -= OnOpenReplied;
                SharedChests.OpenReplied += OnOpenReplied;
                SharedChests.DealReplied -= OnDealReplied;
                SharedChests.DealReplied += OnDealReplied;
            }
        }

        static void Reset()
        {
            s_container = null; s_view = null; s_confirmed = false;
            s_ledger.Expire(float.MaxValue, -1f, null);
            s_templates.Clear(); s_outbox.Clear();
        }

        /// <summary>우리 길로 열 상자인지: 손님(또는 시험 Force)이고 건축물 상자(수레·묘비 빼고).</summary>
        static bool Ours(Container c)
        {
            if (ZNet.instance == null || (ZNet.instance.IsServer() && !Force)) return false;
            if (c == null || c.m_wagon != null || c.GetComponent<Piece>() == null) return false;
            ZNetView v = c.GetComponent<ZNetView>();
            return v != null && v.IsValid();
        }

        /// <summary>손님이 상자를 누름: 바닐라 RPC_RequestOpen 대신 우리 열기(바닐라 Interact와 같은 확인을 먼저).</summary>
        [HarmonyPatch(typeof(Container), "Interact")]
        static class InteractPatch
        {
            static bool Prefix(Container __instance, Humanoid character, bool hold, ref bool __result)
            {
                if (hold || !Ours(__instance)) return true;
                if (__instance.m_checkGuardStone && !PrivateArea.CheckAccess(__instance.transform.position)) { __result = true; return false; }
                if (!CheckAccess(__instance, Game.instance.GetPlayerProfile().GetPlayerID()))
                {
                    character.Message(MessageHud.MessageType.Center, "$msg_cantopen");
                    __result = true;
                    return false;
                }
                Open(__instance);
                __result = true;
                return false;
            }
        }

        /// <summary>열기: 화면은 바로 보여 주고(옮기기는 열림 답이 온 뒤부터) 호스트에 열기 부탁.</summary>
        public static void Open(Container c)
        {
            if (Active) Close();
            s_container = c;
            s_id = c.GetComponent<ZNetView>().GetZDO().m_uid;
            s_confirmed = false;
            s_openedAt = Time.time;
            Inventory own = c.GetInventory();
            if (ZNet.instance.IsServer())
            {
                s_view = new Inventory(own.GetName(), c.m_bkg, own.GetWidth(), own.GetHeight());
                s_rev = uint.MaxValue;
                Refresh();
            }
            else s_view = own;
            Send("VH_ChestOpen", s_id, Game.instance.GetPlayerProfile().GetPlayerID());
            if (InventoryGui.instance != null) InventoryGui.instance.Show(c);
        }

        /// <summary>닫기(창이 닫히거나 다른 상자): 호스트에 닫기만 알림 — 기다리던 부탁 답은 그대로 받는다.</summary>
        public static void Close()
        {
            if (!Active) return;
            Send("VH_ChestClose", s_id);
            s_container = null; s_view = null; s_confirmed = false;
        }

        static void OnOpenReplied(ZDOID id, bool ok)
        {
            if (!Active || id != s_id) return;
            if (ok) { s_confirmed = true; return; }
            Container c = s_container;
            Close();
            if (InventoryGui.instance != null && GuiContainer(InventoryGui.instance) == c) CloseContainer(InventoryGui.instance);
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "$msg_inuse");
        }

        static void Send(string rpc, params object[] args)
        {
            float due = Time.time;
            if (DelayMax > 0f) due += UnityEngine.Random.Range(DelayMin, DelayMax);
            if (due < s_lastDue) due = s_lastDue; // 보낸 순서 그대로
            s_lastDue = due;
            if (due <= Time.time && s_outbox.Count == 0) { ZRoutedRpc.instance.InvokeRoutedRPC(rpc, args); return; }
            s_outbox.Add(new Outgoing { Due = due, Rpc = rpc, Args = args });
        }

        /// <summary>매 프레임: 늦춘 부탁 보내기, 시험 사본 다시 읽기, 창 닫힘·열림 답 없음·답 시간 넘김 처리.</summary>
        public static void Tick()
        {
            if (ZRoutedRpc.instance == null) return;
            while (s_outbox.Count > 0 && s_outbox[0].Due <= Time.time)
            {
                var o = s_outbox[0];
                s_outbox.RemoveAt(0);
                ZRoutedRpc.instance.InvokeRoutedRPC(o.Rpc, o.Args);
            }
            if (s_ledger.Count > 0)
            {
                var back = new List<PutStepBack>();
                if (s_ledger.Expire(Time.time, ReplyTimeout, back) > 0)
                {
                    Plugin.Log.LogWarning($"[ValheimHelper] 손님 상자 부탁 답 없음 — 보낸 것 {back.Count}가지 돌려받음");
                    GiveBack(back);
                }
            }
            if (!Active) return;
            if (s_container == null || InventoryGui.instance == null || GuiContainer(InventoryGui.instance) != s_container) { Close(); return; }
            if (s_view != ContainerInv(s_container)) Refresh();
            if (!s_confirmed && Time.time - s_openedAt > OpenTimeout)
            {
                // 호스트에 우리 모드가 없음 → 바닐라 열기(옮기기는 아직 하나도 안 함)
                Container c = s_container;
                Close();
                CloseContainer(InventoryGui.instance);
                if (!ZNet.instance.IsServer())
                    c.GetComponent<ZNetView>().InvokeRPC("RPC_RequestOpen", Game.instance.GetPlayerProfile().GetPlayerID());
            }
        }

        /// <summary>시험 사본: 호스트 ZDO가 바뀌었으면 다시 읽음(바닐라 Container.Load와 같은 꼴).</summary>
        static void Refresh()
        {
            ZDO zdo = s_container.GetComponent<ZNetView>()?.GetZDO();
            if (zdo == null || zdo.DataRevision == s_rev) return;
            s_rev = zdo.DataRevision;
            byte[] data = zdo.GetByteArray(ZDOVars.s_items);
            if (data != null) s_view.Load(new ZPackage(data));
        }

        // ── 화면: 주인이 아니어도 상자 칸을 보여 줌(바닐라 UpdateContainer는 주인일 때만) ──

        [HarmonyPatch(typeof(InventoryGui), "UpdateContainer")]
        static class UpdateContainerPatch
        {
            static bool Prefix(InventoryGui __instance, Player player)
            {
                if (!Active || GuiContainer(__instance) != s_container) return true;
                if (!GuiAnimator(__instance).GetBool("visible")) return false;
                __instance.m_container.gameObject.SetActive(true);
                InventoryGrid grid = GuiGrid(__instance);
                grid.UpdateInventory(s_view, null, GuiDragItem(__instance));
                __instance.m_containerName.text = Localization.instance.Localize(s_view.GetName());
                if (GuiFirst(__instance)) { grid.ResetView(); GuiFirst(__instance) = false; }
                if (Vector3.Distance(s_container.transform.position, player.transform.position) > __instance.m_autoCloseDistance)
                    CloseContainer(__instance);
                return false;
            }
        }

        // ── 옮기기: 바닐라를 사본 위에서 돌리고 전후 차이를 부탁으로 ──

        sealed class Snap
        {
            public Inventory Own;
            public string[] Keys;
            public int[] Amounts;
            public Dictionary<string, int> Bag;
        }

        static void Read(Inventory inv, out string[] keys, out int[] amounts)
        {
            int w = inv.GetWidth(), n = w * inv.GetHeight();
            keys = new string[n]; amounts = new int[n];
            foreach (var it in inv.GetAllItems())
            {
                int i = it.m_gridPos.y * w + it.m_gridPos.x;
                if (i < 0 || i >= n) continue;
                keys[i] = SharedChests.Key(it); amounts[i] = it.m_stack;
            }
        }

        static Dictionary<string, int> BagCounts()
        {
            var d = new Dictionary<string, int>();
            foreach (var it in Player.m_localPlayer.GetInventory().GetAllItems())
            {
                string k = SharedChests.Key(it);
                d.TryGetValue(k, out int n);
                d[k] = n + it.m_stack;
            }
            return d;
        }

        /// <summary>옮기기 전: 열림 답 전이면 막고, 아니면 상자 Inventory를 사본으로 바꿔 끼우고 전 상태를 적음.</summary>
        static bool Before(InventoryGui gui, out Snap snap)
        {
            snap = null;
            if (!Active || GuiContainer(gui) != s_container || Player.m_localPlayer == null) return true;
            if (!s_confirmed) return false;
            snap = new Snap { Own = ContainerInv(s_container), Bag = BagCounts() };
            Read(s_view, out snap.Keys, out snap.Amounts);
            ContainerInv(s_container) = s_view;
            return true;
        }

        /// <summary>옮기기 뒤: 상자 Inventory를 돌려놓고 차이를 부탁으로 보냄.</summary>
        static void After(Snap snap)
        {
            if (snap == null || s_container == null) return;
            ContainerInv(s_container) = snap.Own;
            Read(s_view, out string[] keys, out int[] amounts);
            var takes = new List<SlotOp>();
            var putOps = new List<SlotOp>();
            ChestDiff.Diff(snap.Keys, snap.Amounts, keys, amounts, takes, putOps);
            if (takes.Count == 0 && putOps.Count == 0) return;
            var after = BagCounts();
            var bag = new Dictionary<string, int>();
            foreach (var kv in after) { snap.Bag.TryGetValue(kv.Key, out int b); if (kv.Value != b) bag[kv.Key] = kv.Value - b; }
            foreach (var kv in snap.Bag) if (!after.ContainsKey(kv.Key)) bag[kv.Key] = -kv.Value;
            int w = s_view.GetWidth();
            var puts = new List<KeyValuePair<int, ItemDrop.ItemData>>();
            var templates = new Dictionary<string, ItemDrop.ItemData>();
            foreach (var op in putOps)
            {
                ItemDrop.ItemData there = s_view.GetItemAt(op.Slot % w, op.Slot / w);
                if (there == null) continue;
                ItemDrop.ItemData one = there.Clone();
                one.m_stack = op.N;
                puts.Add(new KeyValuePair<int, ItemDrop.ItemData>(op.Slot, one));
                templates[op.Key] = one;
            }
            int req = s_ledger.Begin(bag, Time.time);
            s_templates[req] = templates;
            Send("VH_ChestDeal", s_id, SharedChests.DealPackage(req, bag, takes, puts));
        }

        static void OnDealReplied(int req, Dictionary<string, int> chest)
        {
            var fix = s_ledger.Reply(req, chest);
            s_templates.TryGetValue(req, out var templates);
            s_templates.Remove(req);
            if (fix == null || fix.Count == 0 || Player.m_localPlayer == null) return;
            Plugin.Log.LogInfo($"[ValheimHelper] 손님 상자 부탁 {req} 답이 예측과 다름 → 가방 고침: {string.Join(", ", Describe(fix))}");
            Inventory inv = Player.m_localPlayer.GetInventory();
            foreach (var kv in fix)
            {
                if (kv.Value > 0)
                {
                    if (templates != null && templates.TryGetValue(kv.Key, out var t)) Add(inv, t, kv.Value);
                    else Plugin.Log.LogWarning($"[ValheimHelper] 손님 상자: 돌려받을 {kv.Key} {kv.Value}개의 본이 없음");
                    continue;
                }
                int left = -kv.Value;
                foreach (var it in new List<ItemDrop.ItemData>(inv.GetAllItems()))
                {
                    if (left <= 0) break;
                    if (SharedChests.Key(it) != kv.Key) continue;
                    int n = Math.Min(left, it.m_stack);
                    inv.RemoveItem(it, n);
                    left -= n;
                }
            }
        }

        static IEnumerable<string> Describe(Dictionary<string, int> d)
        {
            foreach (var kv in d) yield return kv.Key + " " + (kv.Value > 0 ? "+" : "") + kv.Value;
        }

        /// <summary>시간 넘김: 보낸 것은 넣기에 쓴 본으로 다시 만들어 가방에(본 없으면 로그만).</summary>
        static void GiveBack(List<PutStepBack> back)
        {
            if (Player.m_localPlayer == null) return;
            foreach (var b in back)
            {
                ItemDrop.ItemData t = null;
                foreach (var m in s_templates.Values) if (m.TryGetValue(b.Key, out t)) break;
                if (t != null) Add(Player.m_localPlayer.GetInventory(), t, b.N);
                else Plugin.Log.LogWarning($"[ValheimHelper] 손님 상자: 돌려받을 {b.Key} {b.N}개의 본이 없음");
            }
            if (s_ledger.Count == 0) s_templates.Clear();
        }

        /// <summary>가방에 n개(한 칸 최대씩), 자리 없으면 발밑에 떨굼 — 잃지 않게.</summary>
        static void Add(Inventory inv, ItemDrop.ItemData template, int n)
        {
            int max = Math.Max(1, template.m_shared.m_maxStackSize);
            while (n > 0)
            {
                ItemDrop.ItemData one = template.Clone();
                one.m_stack = Math.Min(n, max);
                n -= one.m_stack;
                if (!inv.AddItem(one))
                {
                    Transform pt = Player.m_localPlayer.transform;
                    ItemDrop.DropItem(one, 0, pt.position + pt.forward + Vector3.up, pt.rotation);
                }
            }
        }


        // ── 답 기다리는 물건(다-3b): 꺼내 온 것은 답 전에 버리기·쓰기·입기·상자에 다시 넣기·제작 재료로 못 씀 ──

        /// <summary>이름(·품질, -1이면 모두)이 맞는 답 기다리는 물건 수.</summary>
        public static int LockedName(string name, int quality)
        {
            if (s_ledger.Count == 0) return 0;
            return s_ledger.LockedWhere(k =>
            {
                string[] p = k.Split('|');
                return (name == null || p[0] == name) && (quality < 0 || (p.Length > 1 && p[1] == quality.ToString()));
            });
        }

        static bool Mine(Humanoid h, Inventory inv) =>
            h != null && h == Player.m_localPlayer && (inv == null || inv == h.GetInventory());

        static bool Blocked(ItemDrop.ItemData item)
        {
            if (s_ledger.Count == 0 || Locked(item) <= 0) return false;
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "상자 답을 기다리는 중…");
            return true;
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.DropItem))]
        static class DropPatch
        {
            static bool Prefix(Humanoid __instance, Inventory inventory, ItemDrop.ItemData item, ref bool __result)
            {
                if (!Mine(__instance, inventory) || !Blocked(item)) return true;
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UseItem))]
        static class UsePatch
        {
            static bool Prefix(Humanoid __instance, Inventory inventory, ItemDrop.ItemData item) =>
                !Mine(__instance, inventory) || !Blocked(item);
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
        static class EquipPatch
        {
            static bool Prefix(Humanoid __instance, ItemDrop.ItemData item, ref bool __result)
            {
                if (!Mine(__instance, null) || !Blocked(item)) return true;
                __result = false;
                return false;
            }
        }

        /// <summary>가방 물건을 상자로(끌어 놓기·자리 바꾸기·Shift 옮기기) — 꺼내기 답이 줄면 가방에서 뺄 물건이라 다시 넣지 못함.</summary>
        static bool PutBlocked(InventoryGui gui, InventoryGrid grid, ItemDrop.ItemData item, InventoryGrid.Modifier mod)
        {
            if (!Active || s_ledger.Count == 0 || grid == null || Player.m_localPlayer == null) return false;
            Inventory bag = Player.m_localPlayer.GetInventory(), target = grid.GetInventory();
            ItemDrop.ItemData drag = GuiDragItem(gui);
            if (drag != null)
            {
                Inventory from = GuiDragInv(gui);
                if (from == bag && target == s_view) return Blocked(drag);
                if (from == s_view && target == bag && item != null) return Blocked(item); // 자리 바꾸기 → 가방 것이 상자로
                return false;
            }
            return mod == InventoryGrid.Modifier.Move && target == bag && Blocked(item);
        }

        /// <summary>제작·건축 재료 셈(아래 범위 안)에서는 답 기다리는 물건을 뺌 — 빼기(ConsumeResources)는 그대로 둬도 남는 수가 잠긴 수 이상.</summary>
        static int s_reqScope;

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.CountItems))]
        static class CountPatch
        {
            static void Postfix(Inventory __instance, string name, int quality, ref int __result)
            {
                if (s_reqScope <= 0 || s_ledger.Count == 0 || Player.m_localPlayer == null || __instance != Player.m_localPlayer.GetInventory()) return;
                __result = Math.Max(0, __result - LockedName(name, quality));
            }
        }

        [HarmonyPatch]
        static class ReqScope
        {
            static IEnumerable<System.Reflection.MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(Player), "HaveRequirementItems");
                yield return AccessTools.Method(typeof(Player), nameof(Player.HaveRequirements), new[] { typeof(Piece), typeof(Player.RequirementMode) });
                yield return AccessTools.Method(typeof(Player), nameof(Player.GetFirstRequiredItem));
                yield return AccessTools.Method(typeof(InventoryGui), nameof(InventoryGui.SetupRequirement));
            }
            static void Prefix() => s_reqScope++;
            static void Finalizer() => s_reqScope--;
        }
        [HarmonyPatch(typeof(InventoryGui), "OnSelectedItem")]
        static class SelectedPatch
        {
            static bool Prefix(InventoryGui __instance, InventoryGrid grid, ItemDrop.ItemData item, InventoryGrid.Modifier mod, out Snap __state)
            {
                __state = null;
                // 상자 물건을 땅에 버리기(Ctrl 클릭)는 가방을 안 거쳐 셈이 안 맞음 → 막음
                if (Active && mod == InventoryGrid.Modifier.Drop && grid != null && grid.GetInventory() == s_view) return false;
                if (PutBlocked(__instance, grid, item, mod)) return false;
                return Before(__instance, out __state);
            }
            static void Postfix(Snap __state) => After(__state);
            static Exception Finalizer(Exception __exception, Snap __state)
            {
                if (__exception != null && __state != null && s_container != null) ContainerInv(s_container) = __state.Own;
                return __exception;
            }
        }

        [HarmonyPatch(typeof(InventoryGui), "OnTakeAll")]
        static class TakeAllPatch
        {
            static bool Prefix(InventoryGui __instance, out Snap __state) => Before(__instance, out __state);
            static void Postfix(Snap __state) => After(__state);
            static Exception Finalizer(Exception __exception, Snap __state)
            {
                if (__exception != null && __state != null && s_container != null) ContainerInv(s_container) = __state.Own;
                return __exception;
            }
        }

        [HarmonyPatch(typeof(InventoryGui), "OnStackAll")]
        static class StackAllPatch
        {
            static bool Prefix(InventoryGui __instance, out Snap __state)
            {
                __state = null;
                if (Active && s_ledger.Count > 0) { Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "상자 답을 기다리는 중…"); return false; }
                return Before(__instance, out __state);
            }
            static void Postfix(Snap __state) => After(__state);
            static Exception Finalizer(Exception __exception, Snap __state)
            {
                if (__exception != null && __state != null && s_container != null) ContainerInv(s_container) = __state.Own;
                return __exception;
            }
        }

        /// <summary>상자 칸에서 끌던 물건을 창 밖에 버리기·상자 물건 바로 쓰기(우클릭)는 막음(가방을 안 거침).</summary>
        [HarmonyPatch(typeof(InventoryGui), "OnDropOutside")]
        static class DropOutsidePatch
        {
            static bool Prefix(InventoryGui __instance) => !(Active && GuiDragInv(__instance) == s_view);
        }

        [HarmonyPatch(typeof(InventoryGui), "OnRightClickItem")]
        static class RightClickPatch
        {
            static bool Prefix(InventoryGrid grid) => !(Active && grid != null && grid.GetInventory() == s_view);
        }
    }
}
