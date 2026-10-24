using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using ValheimHelper.Core.Storage;

namespace ValheimHelper
{
    /// <summary>
    /// 사람끼리 동시 열기(상자 기능 ④ 다-2, 호스트 쪽 — decisions.txt "상자 기능 확정" ④): 우리 모드 손님은 바닐라 RPC_RequestOpen 대신 VH_ChestOpen으로 열고,
    /// 상자 ZDO 주인은 호스트에 둔 채(SetOwner 안 함) 꺼내기·넣기를 VH_ChestDeal로 부탁한다 → 호스트가 Core ChestDeal로 판정해 고치고 옮긴 수를 답(VH_ChestDone).
    /// 모드 없는 손님은 바닐라 그대로(다만 우리 손님이 보는 동안엔 "사용 중"). 손님이 보는 상자는 Chests.InUse = 일꾼이 안 건드림.
    /// 사람이 연 상자도 열어 줌, 같은 물건을 같은 순간 둘이 잡는 복제는 감수(사용자 결정 10-23). 손님 화면(예측)은 SharedGuest(다-3).
    /// </summary>
    public static class SharedChests
    {
        /// <summary>상자 ZDO → 보고 있는 우리 손님 uid(호스트만 채움).</summary>
        static readonly Dictionary<ZDOID, HashSet<long>> s_viewers = new Dictionary<ZDOID, HashSet<long>>();
        /// <summary>시험용 가짜 손님 uid(피어가 없어도 Tick이 안 지움).</summary>
        public static readonly HashSet<long> Fake = new HashSet<long>();
        /// <summary>손님 쪽 답 받기(SharedGuest가 씀): 열림 답, 부탁 답(번호·물건마다 상자가 변한 수).</summary>
        public static event Action<ZDOID, bool> OpenReplied;
        public static event Action<int, Dictionary<string, int>> DealReplied;

        const float CloseDistance = 12f; // 바닐라 m_autoCloseDistance(4m)보다 넉넉히 — 손님 위치(m_refPos)는 늦게 옴
        static float s_nextTick;

        [HarmonyPatch(typeof(Game), "Start")]
        static class GameStart
        {
            static void Postfix()
            {
                s_viewers.Clear();
                ZRoutedRpc.instance.Register<ZDOID, long>("VH_ChestOpen", RPC_Open);
                ZRoutedRpc.instance.Register<ZDOID>("VH_ChestClose", RPC_Close);
                ZRoutedRpc.instance.Register<ZDOID, ZPackage>("VH_ChestDeal", RPC_Deal);
                ZRoutedRpc.instance.Register<ZDOID, bool>("VH_ChestOpenRe", (s, id, ok) => OpenReplied?.Invoke(id, ok));
                ZRoutedRpc.instance.Register<int, ZPackage>("VH_ChestDone", (s, req, pkg) => DealReplied?.Invoke(req, ReadNet(pkg)));
            }
        }

        /// <summary>우리 손님이 이 상자를 보고 있는지(호스트에서만 의미).</summary>
        public static bool GuestViewing(ZDOID id) => s_viewers.TryGetValue(id, out var set) && set.Count > 0;

        /// <summary>물건 키 = 바닐라가 한 더미로 쌓는 조건(이름·품질·월드 단계) + 변형.</summary>
        public static string Key(ItemDrop.ItemData item) =>
            item == null ? null : item.m_shared.m_name + "|" + item.m_quality + "|" + item.m_worldLevel + "|" + item.m_variant;

        /// <summary>넣을 물건 하나를 보내는 꼴(바닐라 Inventory.Save 그대로 — 내구도·제작자·커스텀 데이터 다 감).</summary>
        public static byte[] Pack(ItemDrop.ItemData item)
        {
            var inv = new Inventory("vh_pack", null, 1, 1);
            inv.AddItem(item.Clone(), new Vector2i(0, 0));
            var pkg = new ZPackage();
            inv.Save(pkg);
            return pkg.GetArray();
        }

        public static ItemDrop.ItemData Unpack(byte[] data)
        {
            if (data == null || data.Length == 0) return null;
            var inv = new Inventory("vh_pack", null, 1, 1);
            inv.Load(new ZPackage(data)); // 위치 검사 없이 넣음
            var all = inv.GetAllItems();
            return all.Count == 1 ? all[0] : null;
        }

        // ── 호스트: RPC ──

        static void RPC_Open(long uid, ZDOID id, long playerID)
        {
            if (!ZNet.instance.IsServer()) return;
            bool ok = false;
            try { ok = Open(uid, id, playerID); }
            catch (Exception e) { Plugin.Log.LogWarning($"[ValheimHelper] 손님 상자 열기 오류: {e}"); }
            ZRoutedRpc.instance.InvokeRoutedRPC(uid, "VH_ChestOpenRe", id, ok);
        }

        static void RPC_Close(long uid, ZDOID id)
        {
            if (ZNet.instance.IsServer()) Close(uid, id);
        }

        /// <summary>
        /// 손님 부탁 한 번(화면에서 한 번 옮긴 것): 번호, 물건마다 가방 변화, 꺼내기(칸·키·수), 넣기(칸·물건). 답 = 번호와 물건마다 상자가 실제로 변한 수.
        /// 오류여도 답은 보냄(빈 답 = 아무것도 안 됨 → 손님이 가방을 되돌림).
        /// </summary>
        static void RPC_Deal(long uid, ZDOID id, ZPackage pkg)
        {
            if (!ZNet.instance.IsServer()) return;
            int req = 0;
            var net = new Dictionary<string, int>();
            try
            {
                req = pkg.ReadInt();
                var bag = new Dictionary<string, int>();
                for (int i = pkg.ReadInt(); i > 0; i--) { string k = pkg.ReadString(); bag[k] = pkg.ReadInt(); }
                var takes = new List<SlotOp>();
                for (int i = pkg.ReadInt(); i > 0; i--) { int s = pkg.ReadInt(); string k = pkg.ReadString(); takes.Add(new SlotOp(s, k, pkg.ReadInt())); }
                var puts = new List<KeyValuePair<int, ItemDrop.ItemData>>();
                for (int i = pkg.ReadInt(); i > 0; i--) { int s = pkg.ReadInt(); puts.Add(new KeyValuePair<int, ItemDrop.ItemData>(s, Unpack(pkg.ReadByteArray()))); }
                net = Batch(uid, id, bag, takes, puts);
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[ValheimHelper] 손님 상자 부탁 오류: {e}"); }
            var re = new ZPackage();
            re.Write(net.Count);
            foreach (var kv in net) { re.Write(kv.Key); re.Write(kv.Value); }
            ZRoutedRpc.instance.InvokeRoutedRPC(uid, "VH_ChestDone", req, re);
        }

        static Dictionary<string, int> ReadNet(ZPackage pkg)
        {
            var net = new Dictionary<string, int>();
            for (int i = pkg.ReadInt(); i > 0; i--) { string k = pkg.ReadString(); net[k] = pkg.ReadInt(); }
            return net;
        }

        /// <summary>손님 쪽이 부탁을 보낼 때 쓰는 꼴(RPC_Deal과 같은 순서).</summary>
        public static ZPackage DealPackage(int req, IDictionary<string, int> bag, List<SlotOp> takes, List<KeyValuePair<int, ItemDrop.ItemData>> puts)
        {
            var p = new ZPackage();
            p.Write(req);
            p.Write(bag.Count);
            foreach (var kv in bag) { p.Write(kv.Key); p.Write(kv.Value); }
            p.Write(takes.Count);
            foreach (var t in takes) { p.Write(t.Slot); p.Write(t.Key ?? ""); p.Write(t.N); }
            p.Write(puts.Count);
            foreach (var kv in puts) { p.Write(kv.Key); p.Write(Pack(kv.Value)); }
            return p;
        }

        /// <summary>
        /// 호스트: 부탁 한 번 처리 — 꺼내기를 모두 한 뒤 넣기(그 물건은 이번에 꺼낸 수 + 가방에서 보낸 수까지만, ChestDiff.PutAllowed).
        /// 물건마다 상자가 실제로 변한 수(+ 들어감, - 나감)를 돌려줌.
        /// </summary>
        public static Dictionary<string, int> Batch(long uid, ZDOID id, IDictionary<string, int> bag, List<SlotOp> takes, List<KeyValuePair<int, ItemDrop.ItemData>> puts)
        {
            var taken = new Dictionary<string, int>();
            var put = new Dictionary<string, int>();
            foreach (var t in takes)
            {
                int n = Take(uid, id, t.Slot, t.Key, t.N);
                if (n > 0) { taken.TryGetValue(t.Key, out int had); taken[t.Key] = had + n; }
            }
            foreach (var kv in puts)
            {
                ItemDrop.ItemData item = kv.Value;
                if (item == null) continue;
                string k = Key(item);
                taken.TryGetValue(k, out int got);
                bag.TryGetValue(k, out int moved);
                put.TryGetValue(k, out int done);
                int allow = ChestDiff.PutAllowed(got, -moved, done);
                if (allow <= 0) continue;
                if (item.m_stack > allow) item.m_stack = allow;
                put[k] = done + Put(uid, id, kv.Key, item);
            }
            var net = new Dictionary<string, int>();
            foreach (var kv in taken) net[kv.Key] = -kv.Value;
            foreach (var kv in put) { net.TryGetValue(kv.Key, out int n); n += kv.Value; if (n != 0) net[kv.Key] = n; else net.Remove(kv.Key); }
            return net;
        }

        // ── 호스트: 판정(시험이 바로 부름) ──

        static readonly Func<Container, long, bool> CheckAccess =
            AccessTools.MethodDelegate<Func<Container, long, bool>>(AccessTools.Method(typeof(Container), "CheckAccess"));
        static readonly Func<Container, bool> LoadContainer =
            AccessTools.MethodDelegate<Func<Container, bool>>(AccessTools.Method(typeof(Container), "Load"));
        static readonly Action<Inventory, bool, bool> Changed =
            AccessTools.MethodDelegate<Action<Inventory, bool, bool>>(AccessTools.Method(typeof(Inventory), "Changed"));

        /// <summary>불러온 건축물 상자(수레·묘비 빼고). 없으면 null.</summary>
        static Container Find(ZDOID id)
        {
            GameObject go = ZNetScene.instance.FindInstance(id);
            Container c = go != null ? go.GetComponent<Container>() : null;
            if (c == null || c.m_wagon != null || c.GetComponent<Piece>() == null) return null;
            ZNetView view = c.GetComponent<ZNetView>();
            return view != null && view.IsValid() ? c : null;
        }

        /// <summary>
        /// 고치기 전: 주인을 호스트로(이미면 그대로). 모드 없는 손님이 바닐라로 열어 주인이 그쪽이면(ZDO 사용 중) 못 함 → null.
        /// 사람이 연 상자(호스트·손님)도 고친다 — Chests.TryEdit와 달리 사용 중을 안 봄.
        /// </summary>
        static Container ForEdit(ZDOID id)
        {
            Container c = Find(id);
            if (c == null) return null;
            ZDO zdo = c.GetComponent<ZNetView>().GetZDO();
            if (!zdo.IsOwner())
            {
                if (zdo.GetInt(ZDOVars.s_inUse) == 1) return null;
                zdo.SetOwner(ZDOMan.GetSessionID());
                LoadContainer(c);
            }
            return c;
        }

        /// <summary>손님 열기: 접근 가능하고 모드 없는 사람이 쓰는 중이 아니면 보는 사람에 넣고 true. 주인은 호스트로 둔다.</summary>
        public static bool Open(long uid, ZDOID id, long playerID)
        {
            Container c = ForEdit(id);
            if (c == null || !CheckAccess(c, playerID)) return false;
            ZDOMan.instance.ForceSendZDO(uid, id);
            if (!s_viewers.TryGetValue(id, out var set)) s_viewers[id] = set = new HashSet<long>();
            set.Add(uid);
            return true;
        }

        public static void Close(long uid, ZDOID id)
        {
            if (!s_viewers.TryGetValue(id, out var set)) return;
            set.Remove(uid);
            if (set.Count == 0) s_viewers.Remove(id);
        }

        static bool Viewing(long uid, ZDOID id) => s_viewers.TryGetValue(id, out var set) && set.Contains(uid);

        static ItemDrop.ItemData At(Inventory inv, int slot)
        {
            int w = inv.GetWidth();
            return slot < 0 || w <= 0 ? null : inv.GetItemAt(slot % w, slot / w);
        }

        /// <summary>손님 꺼내기: 칸 slot에서 key 물건 want개. 실제로 뺀 수(손님은 그만큼만 가짐).</summary>
        public static int Take(long uid, ZDOID id, int slot, string key, int want)
        {
            if (!Viewing(uid, id)) return 0;
            Container c = ForEdit(id);
            if (c == null) return 0;
            Inventory inv = c.GetInventory();
            ItemDrop.ItemData item = At(inv, slot);
            int n = ChestDeal.Take(Key(item), item != null ? item.m_stack : 0, key, want);
            if (n > 0) inv.RemoveItem(item, n); // 주인이라 OnContainerChanged가 저장
            return n;
        }

        /// <summary>손님 넣기: 놓은 칸 → 같은 물건 칸 → 빈칸. 실제로 넣은 수(나머지는 손님이 돌려받음).</summary>
        public static int Put(long uid, ZDOID id, int slot, ItemDrop.ItemData item)
        {
            if (item == null || item.m_stack <= 0 || !Viewing(uid, id)) return 0;
            Container c = ForEdit(id);
            if (c == null) return 0;
            Inventory inv = c.GetInventory();
            int w = inv.GetWidth(), h = inv.GetHeight();
            var keys = new string[w * h];
            var amounts = new int[w * h];
            foreach (var it in inv.GetAllItems())
            {
                int i = it.m_gridPos.y * w + it.m_gridPos.x;
                if (i < 0 || i >= keys.Length) continue;
                keys[i] = Key(it); amounts[i] = it.m_stack;
            }
            string key = Key(item);
            var plan = ChestDeal.Put(keys, amounts, Math.Max(1, item.m_shared.m_maxStackSize), slot, key, item.m_stack, out int leftover);
            int done = 0;
            foreach (var step in plan)
            {
                ItemDrop.ItemData there = At(inv, step.Slot);
                if (there != null)
                {
                    there.m_stack += step.N; // 같은 물건 더미(Put이 키로 고름)
                    Changed(inv, true, false);
                }
                else
                {
                    ItemDrop.ItemData one = item.Clone();
                    one.m_stack = step.N;
                    one.m_gridPos = new Vector2i(step.Slot % w, step.Slot / w);
                    if (!inv.AddItem(one, one.m_gridPos)) continue;
                }
                done += step.N;
            }
            return done;
        }

        /// <summary>호스트 매 프레임(1초마다): 떠난 손님·멀어진 손님을 보는 사람에서 뺌(닫기 RPC를 못 받아도 일꾼이 영영 못 쓰지 않게).</summary>
        public static void Tick()
        {
            if (s_viewers.Count == 0 || Time.time < s_nextTick || ZNet.instance == null || !ZNet.instance.IsServer()) return;
            s_nextTick = Time.time + 1f;
            var drop = new List<KeyValuePair<ZDOID, long>>();
            foreach (var kv in s_viewers)
            {
                ZDO zdo = ZDOMan.instance.GetZDO(kv.Key);
                foreach (long uid in kv.Value)
                {
                    if (Fake.Contains(uid)) continue;
                    ZNetPeer peer = ZNet.instance.GetPeer(uid);
                    if (zdo == null || peer == null || Vector3.Distance(peer.m_refPos, zdo.GetPosition()) > CloseDistance)
                        drop.Add(new KeyValuePair<ZDOID, long>(kv.Key, uid));
                }
            }
            foreach (var d in drop) Close(d.Value, d.Key);
        }

        /// <summary>시험 끝: 가짜 손님과 그들이 보던 것 지움.</summary>
        public static void ClearFakes()
        {
            foreach (var kv in new List<KeyValuePair<ZDOID, HashSet<long>>>(s_viewers))
                foreach (long uid in Fake) Close(uid, kv.Key);
            Fake.Clear();
        }

        /// <summary>우리 손님이 보는 동안 모드 없는 사람의 바닐라 열기·모두 쌓기·모두 꺼내기는 "사용 중"(주인이 그쪽으로 넘어가지 않게). 호스트 자신은 그대로.</summary>
        [HarmonyPatch(typeof(Container), "RPC_RequestOpen")]
        static class RequestOpen
        {
            static bool Prefix(Container __instance, long uid) => Allow(__instance, uid, "RPC_OpenResponse");
        }

        [HarmonyPatch(typeof(Container), "RPC_RequestStack")]
        static class RequestStack
        {
            static bool Prefix(Container __instance, long uid) => Allow(__instance, uid, "RPC_StackResponse");
        }

        [HarmonyPatch(typeof(Container), "RPC_RequestTakeAll")]
        static class RequestTakeAll
        {
            static bool Prefix(Container __instance, long uid) => Allow(__instance, uid, "RPC_TakeAllResponse");
        }

        static bool Allow(Container c, long uid, string response)
        {
            ZNetView view = c.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || !view.IsOwner() || uid == ZNet.GetUID() || !GuestViewing(view.GetZDO().m_uid)) return true;
            view.InvokeRPC(uid, response, false);
            return false;
        }
    }
}
