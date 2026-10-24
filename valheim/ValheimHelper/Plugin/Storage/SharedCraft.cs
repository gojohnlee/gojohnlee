using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 친구(손님) 제작대 제작 재료 부탁(상자 기능 ④ 다-4a, decisions.txt "상자 기능 확정" ④): 손님도 거점 상자 재료로 제작 목록·개수를 본다(손님 쪽 상자 사본으로 셈).
    /// 제작 막대가 시작될 때(OnCraftPressed) 가방에 모자라는 몫을 호스트에 부탁(VH_CraftTake) → 호스트가 그 손님 자리 기준 거점 상자에서 빼서
    /// 물건째 돌려줌(VH_CraftGive) → 가방에 넣음. 막대(기본 2초) 안에 답이 와서 바닐라와 같은 속도. 막대 끝(DoCrafting)은 바닐라 그대로 가방만 보고 만듦
    /// (손님은 이때 상자를 안 셈 — 상자 몫이 안 왔으면 안 만들어져 복제 없음). 답이 늦으면 막대를 끝에 잡아 두고 기다림(HoldTimeout).
    /// 막대를 취소해도 받은 재료는 가방에 남음(잃지 않음). 호스트에 우리 모드가 없으면(답이 한 번도 없음) 이 접속 동안 손님 상자 셈을 끔.
    /// 건축(망치, 다-4b): 손님이 아는 상자 내용(BaseStock.Count)으로 바로 놓고, 놓은 순간 가방 몫만 빼고 상자 몫은 호스트에 부탁(VH_BuildTake)
    /// → 호스트가 전부 있을 때만 뺌(VH_BuildDone ok). 모자라면(드묾)·답이 BuildTimeout 안에 없으면 손님이 그 조각을 걷고(떨굼 없이) 뺀 가방 몫을 돌려받음.
    /// 망치 Hud 개수 표시도 가방 + 상자.
    /// </summary>
    public static class SharedCraft
    {
        const float HoldTimeout = 5f;

        static int s_next = 1;
        static readonly Dictionary<int, float> s_pending = new Dictionary<int, float>();
        static readonly Dictionary<int, int> s_asked = new Dictionary<int, int>();
        static bool s_replied, s_hold;
        static int s_doCraft;
        /// <summary>호스트에서 답이 안 옴 → 손님 상자 셈 끔(이 접속 동안).</summary>
        public static bool HostNoMod;

        static readonly AccessTools.FieldRef<InventoryGui, float> s_timer = AccessTools.FieldRefAccess<InventoryGui, float>("m_craftTimer");
        static readonly AccessTools.FieldRef<InventoryGui, Recipe> s_recipe = AccessTools.FieldRefAccess<InventoryGui, Recipe>("m_craftRecipe");
        static readonly AccessTools.FieldRef<InventoryGui, ItemDrop.ItemData> s_upgrade = AccessTools.FieldRefAccess<InventoryGui, ItemDrop.ItemData>("m_craftUpgradeItem");
        static readonly AccessTools.FieldRef<InventoryGui, bool> s_multi = AccessTools.FieldRefAccess<InventoryGui, bool>("m_multiCrafting");

        public static int Pending => s_pending.Count;
        /// <summary>막 막대 끝에서 답을 기다리느라 제작을 미룸(시험이 봄).</summary>
        public static bool Held;

        /// <summary>손님(또는 가짜 손님 시험)의 자기 플레이어.</summary>
        public static bool Guest(Player p) =>
            p != null && p == Player.m_localPlayer && ZNet.instance != null && ZNetScene.instance != null && ZRoutedRpc.instance != null
            && (!ZNet.instance.IsServer() || SharedGuest.Force) && !HostNoMod;

        /// <summary>제작 목록 판단에 상자를 셈: 손님이고 막대 끝(DoCrafting)이 아닐 때.</summary>
        public static bool Counts(Player p) => s_doCraft == 0 && Guest(p);
        /// <summary>개수 표시(제작 목록·망치 Hud).</summary>
        public static bool CountsShown(Player p) => Counts(p);

        [HarmonyPatch(typeof(Game), "Start")]
        static class GameStart
        {
            static void Postfix()
            {
                s_pending.Clear(); s_asked.Clear(); s_builds.Clear();
                s_replied = s_hold = Held = HostNoMod = false;
                s_doCraft = s_placing = 0;
                s_placed = null;
                ZRoutedRpc.instance.Register<ZPackage>("VH_CraftTake", RPC_Take);
                ZRoutedRpc.instance.Register<int, ZPackage>("VH_CraftGive", RPC_Give);
                ZRoutedRpc.instance.Register<ZPackage>("VH_BuildTake", RPC_BuildTake);
                ZRoutedRpc.instance.Register<int, bool>("VH_BuildDone", RPC_BuildDone);
            }
        }

        // ── 손님 ──

        [HarmonyPatch(typeof(InventoryGui), "OnCraftPressed")]
        static class Pressed
        {
            static void Postfix(InventoryGui __instance) => Started(__instance);
        }

        /// <summary>막대가 막 시작됐으면(m_craftTimer 0) 가방에 모자라는 몫을 호스트에 부탁. 시험이 바로 부름.</summary>
        public static void Started(InventoryGui gui)
        {
            Player p = Player.m_localPlayer;
            if (gui == null || s_timer(gui) != 0f || !Guest(p)) return;
            Recipe rec = s_recipe(gui);
            if (rec == null || rec.m_requireOnlyOneIngredient) return;
            ItemDrop.ItemData up = s_upgrade(gui);
            int quality = up == null ? 1 : up.m_quality + 1;
            int mult = s_multi(gui) ? gui.m_multiCraftAmount : 1;
            CraftingStation station = p.GetCurrentCraftingStation();
            var need = new Dictionary<string, int>();
            foreach (var r in rec.m_resources)
            {
                if (!CraftPatches.Counts(station, r)) continue;
                string name = r.m_resItem.m_itemData.m_shared.m_name;
                need.TryGetValue(name, out int n);
                need[name] = n + r.GetAmount(quality) * mult;
            }
            Inventory bag = p.GetInventory();
            var pkg = new ZPackage();
            var list = new List<KeyValuePair<string, int>>();
            int total = 0;
            foreach (var kv in need)
            {
                int have = bag.CountItems(kv.Key) - SharedGuest.LockedName(kv.Key, -1);
                int shortBy = kv.Value - Mathf.Max(0, have);
                if (shortBy > 0) { list.Add(new KeyValuePair<string, int>(kv.Key, shortBy)); total += shortBy; }
            }
            if (list.Count == 0) return;
            int req = s_next++;
            pkg.Write(req);
            pkg.Write(p.transform.position);
            pkg.Write(p.GetPlayerID());
            pkg.Write(list.Count);
            foreach (var kv in list) { pkg.Write(kv.Key); pkg.Write(kv.Value); }
            s_pending[req] = Time.time;
            s_asked[req] = total;
            Plugin.Log.LogInfo($"[ValheimHelper] 손님 제작 재료 부탁 {req}: {string.Join(", ", list.ConvertAll(kv => kv.Key + " " + kv.Value))}");
            SharedGuest.Send("VH_CraftTake", pkg);
        }

        static void RPC_Give(long sender, int req, ZPackage pkg)
        {
            s_replied = true;
            s_pending.Remove(req);
            s_asked.TryGetValue(req, out int asked);
            s_asked.Remove(req);
            Player p = Player.m_localPlayer;
            int got = 0;
            try
            {
                for (int i = pkg.ReadInt(); i > 0; i--)
                {
                    ItemDrop.ItemData item = SharedChests.Unpack(pkg.ReadByteArray());
                    if (item == null || p == null) continue;
                    got += item.m_stack;
                    SharedGuest.Add(p.GetInventory(), item, item.m_stack);
                }
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"[ValheimHelper] 손님 제작 재료 답 오류: {e}"); }
            Plugin.Log.LogInfo($"[ValheimHelper] 손님 제작 재료 답 {req}: {got}/{asked}개 받음");
            if (got < asked) p?.Message(MessageHud.MessageType.Center, "상자 재료가 그사이 모자라졌어요");
        }

        /// <summary>
        /// 막대 끝: 손님은 가방만 셈(s_doCraft). 답을 기다리는 중이면 이번엔 만들지 않고(제작법을 잠깐 비움 — DoCrafting이 바로 돌아감)
        /// 막대를 끝에 잡아 둠(UpdateRecipe 뒤 다시 채움) → 다음 프레임에 다시 시도. HoldTimeout 넘으면 기다리지 않음.
        /// </summary>
        [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
        static class Craft
        {
            static void Prefix(InventoryGui __instance, Player player, out Recipe __state)
            {
                __state = null;
                s_doCraft++;
                if (!Guest(player) || s_pending.Count == 0) return;
                float oldest = float.MaxValue;
                foreach (var t in s_pending.Values) oldest = Mathf.Min(oldest, t);
                if (Time.time - oldest < HoldTimeout)
                {
                    __state = s_recipe(__instance);
                    s_recipe(__instance) = null;
                    s_hold = Held = true;
                    return;
                }
                Plugin.Log.LogWarning($"[ValheimHelper] 손님 제작 재료 답 {s_pending.Count}개가 {HoldTimeout}초 넘게 안 옴 — 기다리지 않음");
                if (!s_replied) HostNoMod = true;
                s_pending.Clear(); s_asked.Clear();
            }
            static void Finalizer(InventoryGui __instance, Recipe __state)
            {
                s_doCraft--;
                if (__state != null) s_recipe(__instance) = __state;
            }
        }

        [HarmonyPatch(typeof(InventoryGui), "UpdateRecipe")]
        static class Update
        {
            static void Postfix(InventoryGui __instance)
            {
                if (!s_hold) return;
                s_hold = false;
                if (s_recipe(__instance) != null) s_timer(__instance) = 1000f; // 막대 끝(꽉 참)에 잡아 둠
            }
        }

        // ── 손님 건축(다-4b) ──

        const float BuildTimeout = 5f;

        class Build
        {
            public Piece Piece;
            public float At;
            public readonly List<ItemDrop.ItemData> FromBag = new List<ItemDrop.ItemData>();
        }

        static readonly Dictionary<int, Build> s_builds = new Dictionary<int, Build>();
        static int s_placing;
        static Piece s_placed;

        public static int BuildPending => s_builds.Count;

        /// <summary>놓기 클릭(UpdatePlacement → TryPlacePiece → PlacePiece → ConsumeResources가 같은 프레임) 범위.</summary>
        [HarmonyPatch(typeof(Player), "UpdatePlacement")]
        static class PlaceScope
        {
            static void Prefix() { s_placing++; s_placed = null; }
            static void Finalizer() { if (--s_placing <= 0) { s_placing = 0; s_placed = null; } }
        }

        /// <summary>PlacePiece가 새 조각에 주인을 적는 순간 = 방금 놓은 조각.</summary>
        [HarmonyPatch(typeof(Piece), nameof(Piece.SetCreator))]
        static class Placed
        {
            static void Postfix(Piece __instance) { if (s_placing > 0) s_placed = __instance; }
        }

        /// <summary>시험: 바닐라 놓기 클릭과 같은 순서(PlacePiece → ConsumeResources)를 놓기 범위 안에서. 놓은 조각을 돌려줌.</summary>
        internal static Piece TestPlace(Player p, Piece piece, Vector3 pos, Quaternion rot)
        {
            s_placing++;
            s_placed = null;
            try
            {
                p.PlacePiece(piece, pos, rot, doAttack: false);
                Piece made = s_placed;
                p.ConsumeResources(piece.m_resources, 0);
                return made;
            }
            finally { s_placing--; s_placed = null; }
        }

        /// <summary>
        /// 손님이 놓은 조각의 재료 빼기: 가방(답 기다리는 물건 뺀 수)에 다 있으면 바닐라 그대로. 모자라면 가방 몫만 빼서 기억하고 상자 몫은 호스트에 부탁.
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.ConsumeResources))]
        static class BuildConsume
        {
            static bool Prefix(Player __instance, Piece.Requirement[] requirements, int qualityLevel, int itemQuality, int multiplier)
            {
                if (s_placing <= 0 || s_placed == null || requirements == null || s_doCraft > 0 || !Guest(__instance)) return true;
                CraftingStation station = __instance.GetCurrentCraftingStation();
                Inventory bag = __instance.GetInventory();
                var need = new Dictionary<string, int>();
                foreach (var r in requirements)
                {
                    if (!CraftPatches.Counts(station, r)) continue;
                    int n = r.GetAmount(qualityLevel) * multiplier;
                    if (n <= 0) continue;
                    string name = r.m_resItem.m_itemData.m_shared.m_name;
                    need.TryGetValue(name, out int had);
                    need[name] = had + n;
                }
                var shortList = new List<KeyValuePair<string, int>>();
                var fromBag = new Dictionary<string, int>();
                foreach (var kv in need)
                {
                    int free = Mathf.Max(0, bag.CountItems(kv.Key, itemQuality) - SharedGuest.LockedName(kv.Key, itemQuality));
                    int mine = Mathf.Min(free, kv.Value);
                    fromBag[kv.Key] = mine;
                    if (kv.Value > mine) shortList.Add(new KeyValuePair<string, int>(kv.Key, kv.Value - mine));
                }
                if (shortList.Count == 0) return true;
                var b = new Build { Piece = s_placed, At = Time.time };
                foreach (var kv in fromBag) TakeBag(bag, kv.Key, kv.Value, itemQuality, b.FromBag);
                int req = s_next++;
                s_builds[req] = b;
                var pkg = new ZPackage();
                pkg.Write(req);
                pkg.Write(__instance.transform.position);
                pkg.Write(__instance.GetPlayerID());
                pkg.Write(shortList.Count);
                foreach (var kv in shortList) { pkg.Write(kv.Key); pkg.Write(kv.Value); }
                Plugin.Log.LogInfo($"[ValheimHelper] 손님 건축 재료 부탁 {req} ({s_placed.m_name}): {string.Join(", ", shortList.ConvertAll(kv => kv.Key + " " + kv.Value))}");
                SharedGuest.Send("VH_BuildTake", pkg);
                return false;
            }
        }

        /// <summary>가방에서 k개 빼고 뺀 것을 더미째(복제본) 기억 — 걷을 때 돌려줌.</summary>
        static void TakeBag(Inventory bag, string name, int k, int quality, List<ItemDrop.ItemData> into)
        {
            foreach (var it in new List<ItemDrop.ItemData>(bag.GetAllItems()))
            {
                if (k <= 0) break;
                if (it.m_shared.m_name != name || (quality >= 0 && it.m_quality != quality)) continue;
                int m = Mathf.Min(k, it.m_stack);
                ItemDrop.ItemData one = it.Clone();
                one.m_stack = m;
                bag.RemoveItem(it, m);
                into.Add(one);
                k -= m;
            }
        }

        static void RPC_BuildDone(long sender, int req, bool ok)
        {
            s_replied = true;
            if (!s_builds.TryGetValue(req, out Build b)) return;
            s_builds.Remove(req);
            Plugin.Log.LogInfo($"[ValheimHelper] 손님 건축 재료 답 {req}: {(ok ? "뺌" : "모자람")}");
            if (!ok) Undo(b, "상자 재료가 그사이 모자라 조각을 걷었어요");
        }

        /// <summary>조각을 떨굼 없이 걷고 가방 몫을 돌려줌. 조각이 이미 없으면(부숴서 재료가 나왔을 수 있음) 돌려주지 않음.</summary>
        static void Undo(Build b, string msg)
        {
            Player p = Player.m_localPlayer;
            if (b.Piece == null || p == null) return;
            ZNetView view = b.Piece.GetComponent<ZNetView>();
            if (view != null && view.IsValid()) view.ClaimOwnership();
            ZNetScene.instance.Destroy(b.Piece.gameObject);
            foreach (var it in b.FromBag) SharedGuest.Add(p.GetInventory(), it, it.m_stack);
            p.Message(MessageHud.MessageType.Center, msg);
        }

        /// <summary>매 프레임: 건축 답이 BuildTimeout 넘게 없으면 걷음(한 번도 답이 없었으면 호스트에 우리 모드 없음).</summary>
        public static void Tick()
        {
            if (s_builds.Count == 0) return;
            List<int> late = null;
            foreach (var kv in s_builds) if (Time.time - kv.Value.At > BuildTimeout) (late = late ?? new List<int>()).Add(kv.Key);
            if (late == null) return;
            Plugin.Log.LogWarning($"[ValheimHelper] 손님 건축 재료 답 {late.Count}개가 {BuildTimeout}초 넘게 안 옴 — 조각을 걷음");
            if (!s_replied) HostNoMod = true;
            foreach (int req in late) { Build b = s_builds[req]; s_builds.Remove(req); Undo(b, "호스트 답이 없어 조각을 걷었어요"); }
        }

        // ── 호스트 ──

        static void RPC_Take(long uid, ZPackage pkg)
        {
            if (!ZNet.instance.IsServer()) return;
            int req = 0;
            var got = new List<ItemDrop.ItemData>();
            try
            {
                req = pkg.ReadInt();
                Vector3 pos = pkg.ReadVector3();
                long playerID = pkg.ReadLong();
                for (int i = pkg.ReadInt(); i > 0; i--)
                {
                    string name = pkg.ReadString();
                    int n = Mathf.Clamp(pkg.ReadInt(), 0, 10000);
                    BaseStock.TakeFor(pos, playerID, name, n, got);
                }
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"[ValheimHelper] 손님 제작 재료 부탁 오류: {e}"); }
            var re = new ZPackage();
            re.Write(got.Count);
            foreach (var it in got) re.Write(SharedChests.Pack(it));
            ZRoutedRpc.instance.InvokeRoutedRPC(uid, "VH_CraftGive", req, re);
        }

        /// <summary>호스트: 손님 건축 재료(다-4b) — 그 손님 자리 거점 상자에 전부 있을 때만 뺌(캐시 없이 지금 셈), 하나라도 모자라면 아무것도 안 뺌.</summary>
        static void RPC_BuildTake(long uid, ZPackage pkg)
        {
            if (!ZNet.instance.IsServer()) return;
            int req = 0;
            bool ok = false;
            try
            {
                req = pkg.ReadInt();
                Vector3 pos = pkg.ReadVector3();
                long playerID = pkg.ReadLong();
                var list = new List<KeyValuePair<string, int>>();
                for (int i = pkg.ReadInt(); i > 0; i--) list.Add(new KeyValuePair<string, int>(pkg.ReadString(), Mathf.Clamp(pkg.ReadInt(), 0, 10000)));
                ok = true;
                foreach (var kv in list) if (BaseStock.CountFor(pos, playerID, kv.Key) < kv.Value) { ok = false; break; }
                if (ok) foreach (var kv in list) BaseStock.TakeFor(pos, playerID, kv.Key, kv.Value, null);
            }
            catch (System.Exception e) { ok = false; Plugin.Log.LogWarning($"[ValheimHelper] 손님 건축 재료 부탁 오류: {e}"); }
            ZRoutedRpc.instance.InvokeRoutedRPC(uid, "VH_BuildDone", req, ok);
        }
    }
}
