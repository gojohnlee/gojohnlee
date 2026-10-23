using System.Collections.Generic;
using HarmonyLib;
using ValheimHelper.Core.Storage;
using ValheimHelper.Core.Work;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 상자 재료로 바로 제작(상자 기능 ①, 사용자 결정 2026-10-23, decisions.txt "상자 기능 확정"): 가까운 깃발 125m(BaseArea) 안 상자 재료를
    /// 사람 제작에 바로 쓴다. 내 가방 먼저 → 가까운 상자부터. 이름 역할·개인·보물 무관(남이 지은 개인 상자만 뺌 — 기본안), 누가 열고 있는 상자는 뺌.
    /// 호스트(싱글 포함)의 자기 플레이어만 — 손님은 상자 내용이 호스트 것이라 친구 단계에서. 불러와진 상자만(빼기가 TryEdit라 인스턴스가 있어야 함).
    /// 세기는 0.5초 캐시(바닐라가 제작 목록을 매 프레임 확인). 실제 제작(DoCrafting) 동안은 캐시 없이 그 프레임에 세고 빼서
    /// "있다고 봤는데 사이에 빠져 덜 빠짐(물건 복제)"을 막는다.
    /// </summary>
    public static class BaseStock
    {
        const float CacheTime = 0.5f;

        static float s_at = -100f;
        static int s_frame = -1;
        static Player s_for;
        static readonly List<Container> s_chests = new List<Container>();
        static readonly Dictionary<string, int> s_counts = new Dictionary<string, int>();
        static float s_flagAt = -100f;
        static readonly List<Vector3> s_flags = new List<Vector3>();

        /// <summary>0보다 크면 캐시를 안 씀(제작 막대 끝 DoCrafting 동안).</summary>
        internal static int Fresh;

        public static bool Active(Player p) =>
            p != null && p == Player.m_localPlayer && ZNet.instance != null && ZNet.instance.IsServer() && ZNetScene.instance != null;

        /// <summary>플레이어에게서 가장 가까운 거점 깃발(125m 안), 없으면 null. 깃발 목록은 5초 캐시(전체 ZDO 훑기라).</summary>
        static Vector3? Home(Vector3 pos)
        {
            if (Time.time - s_flagAt > 5f)
            {
                s_flagAt = Time.time;
                s_flags.Clear();
                foreach (var z in BaseFlag.AllFlags()) s_flags.Add(z.GetPosition());
            }
            Vector3? best = null;
            float bestD = float.MaxValue;
            foreach (var f in s_flags)
            {
                float d = Utils.DistanceXZ(f, pos);
                if (BaseArea.Contains(d) && d < bestD) { bestD = d; best = f; }
            }
            return best;
        }

        /// <summary>남이 지은 개인·보물 상자(내 제작에 안 씀, 기본안).</summary>
        static bool OthersPrivate(ZDO zdo, Player p)
        {
            if ((ChestTagReader.Read(zdo) & ChestTags.Private) == 0) return false;
            long creator = zdo.GetLong(ZDOVars.s_creator);
            return creator != 0 && creator != p.GetPlayerID();
        }

        static void Refresh(Player p)
        {
            bool fresh = Fresh > 0 ? s_frame != Time.frameCount : Time.time - s_at > CacheTime;
            if (!fresh && s_for == p) return;
            s_at = Time.time;
            s_frame = Time.frameCount;
            s_for = p;
            s_chests.Clear();
            s_counts.Clear();
            Vector3? home = Home(p.transform.position);
            if (!home.HasValue) return;
            foreach (var zdo in Senses.Nearby(home.Value, BaseArea.Radius, Chests.IsChest))
            {
                ZNetView view = ZNetScene.instance.FindInstance(zdo);
                if (view == null) continue;
                Container c = view.GetComponent<Container>();
                if (c == null || Chests.InUse(c) || OthersPrivate(zdo, p)) continue;
                s_chests.Add(c);
            }
            Vector3 me = p.transform.position;
            s_chests.Sort((a, b) => Utils.DistanceXZ(a.transform.position, me).CompareTo(Utils.DistanceXZ(b.transform.position, me)));
        }

        /// <summary>쓸 수 있는 상자들(가까운 순).</summary>
        public static List<Container> Usable(Player p)
        {
            Refresh(p);
            return new List<Container>(s_chests);
        }

        /// <summary>쓸 수 있는 상자 전부의 그 물건 수(m_shared.m_name).</summary>
        public static int Count(Player p, string name)
        {
            Refresh(p);
            if (s_counts.TryGetValue(name, out int n)) return n;
            n = 0;
            foreach (var c in s_chests) if (c != null) n += c.GetInventory().CountItems(name);
            s_counts[name] = n;
            return n;
        }

        /// <summary>상자 내용이 바뀌었으니 다음 셈은 다시(일꾼이 같은 프레임에 꺼낸 뒤).</summary>
        internal static void Changed() { s_at = -100f; s_frame = -1; }

        static readonly AccessTools.FieldRef<InventoryGui, float> s_timer = AccessTools.FieldRefAccess<InventoryGui, float>("m_craftTimer");
        static readonly AccessTools.FieldRef<InventoryGui, Recipe> s_recipe = AccessTools.FieldRefAccess<InventoryGui, Recipe>("m_craftRecipe");
        static readonly AccessTools.FieldRef<InventoryGui, ItemDrop.ItemData> s_upgrade = AccessTools.FieldRefAccess<InventoryGui, ItemDrop.ItemData>("m_craftUpgradeItem");
        static readonly AccessTools.FieldRef<InventoryGui, bool> s_multi = AccessTools.FieldRefAccess<InventoryGui, bool>("m_multiCrafting");

        /// <summary>
        /// 예약(① 라, 사용자 요청 10-23): 호스트 플레이어의 제작 막대가 도는 동안(m_craftTimer ≥ 0) 그 제작 재료 중 가방에 없는 몫.
        /// 막대가 끝나거나(DoCrafting 뒤 −1)·취소·창 닫힘(바닐라가 −1로)이면 바로 0 — 따로 풀기·안전 시간 없이 매번 지금 막대에서 셈(기본안).
        /// </summary>
        public static int Reserved(string name)
        {
            Player p = Player.m_localPlayer;
            InventoryGui gui = InventoryGui.instance;
            if (gui == null || name == null || !Active(p) || s_timer(gui) < 0f) return 0;
            Recipe rec = s_recipe(gui);
            if (rec == null || rec.m_requireOnlyOneIngredient) return 0;
            ItemDrop.ItemData up = s_upgrade(gui);
            int quality = up == null ? 1 : up.m_quality + 1;
            int mult = s_multi(gui) ? gui.m_multiCraftAmount : 1;
            CraftingStation station = p.GetCurrentCraftingStation();
            int need = 0;
            foreach (var r in rec.m_resources)
                if (CraftPatches.Counts(station, r) && r.m_resItem.m_itemData.m_shared.m_name == name) need += r.GetAmount(quality) * mult;
            return Mathf.Max(0, need - p.GetInventory().CountItems(name));
        }

        /// <summary>
        /// 일꾼이 상자 c에서 name을 want개 꺼내려 할 때 꺼내도 되는 수: c가 예약 상자들(플레이어 거점의 쓸 수 있는 상자) 안이면 "합계 − 예약"까지만.
        /// 상자끼리 옮기는 정리는 합계 그대로라 이걸 안 거침. 꺼낸 뒤엔 Changed()로 다음 셈을 새로.
        /// </summary>
        internal static int Allow(Container c, string name, int want)
        {
            if (want <= 0) return 0;
            int reserved = Reserved(name);
            if (reserved <= 0) return want;
            Player p = Player.m_localPlayer;
            Fresh++;
            try
            {
                Refresh(p);
                if (!s_chests.Contains(c)) return want;
                return StockTake.Spare(Count(p, name), reserved, want);
            }
            finally { Fresh--; }
        }

        /// <summary>
        /// 상자(가까운 순)에서 n개를 뺀다. 실제로 뺀 수. 모자라면 있는 만큼만 빼고 경고(DoCrafting이 같은 프레임에 확인하므로 정상이면 안 생김).
        /// </summary>
        public static int Take(Player p, string name, int n)
        {
            if (n <= 0) return 0;
            Refresh(p);
            var chests = new List<Container>(s_chests);
            var have = new int[chests.Count];
            int total = 0;
            for (int i = 0; i < chests.Count; i++) { have[i] = chests[i] != null ? chests[i].GetInventory().CountItems(name) : 0; total += have[i]; }
            int[] plan = StockTake.Plan(Mathf.Min(n, total), 0, have);
            int removed = 0;
            if (plan != null)
                for (int i = 0; i < chests.Count; i++)
                {
                    int k = plan[i];
                    if (k <= 0) continue;
                    Container c = chests[i];
                    int before = 0;
                    if (Chests.TryEdit(c, inv => { before = inv.CountItems(name); inv.RemoveItem(name, k); }))
                        removed += before - c.GetInventory().CountItems(name);
                }
            s_at = -100f; // 바뀐 개수를 다음에 다시 셈
            s_frame = -1;
            if (removed < n) Plugin.Log.LogWarning($"상자 재료: {name} {n}개 중 {removed}개만 뺌");
            return removed;
        }
    }
}
