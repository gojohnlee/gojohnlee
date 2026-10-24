using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>Hunt: 캐거나 줍는 게 아니라 사냥감이 떨어뜨리는 물건(⑧). 도구 대신 무기로 잡는다.</summary>
    public enum ToolKind { None, Axe, Pickaxe, Hunt }

    /// <summary>캘 수 있는 것 하나(나무, 통나무, 그루터기, 바위 …)의 바닐라 정보.</summary>
    public sealed class ResourceInfo
    {
        public string Prefab;
        public ToolKind Tool;
        public int MinToolTier;
        /// <summary>쓰러뜨리면(쪼개면) 결국 나오는 물건 프리팹 이름들.</summary>
        public readonly HashSet<string> Produces = new HashSet<string>();
        /// <summary>고를 때 가산점(m): 쓰러진 통나무 30 > 큰 나무·바위 10 > 줍는 것 5 > 어린 나무·그루터기·작은 돌 0.</summary>
        public float Priority;
        /// <summary>도구 없이 손으로 줍는 것(Pickable).</summary>
        public bool Pickable;
        /// <summary>서 있는 나무: 쓰러뜨리면 통나무가 넘어지며 굴러 사람을 친다(바닐라 TreeLog 충돌 피해).</summary>
        public bool Falls;
        /// <summary>쓰러진 통나무(TreeLog, 반토막 포함): 구르는 동안 부딪히면 사람을 친다(ImpactEffect).</summary>
        public bool Log;
        /// <summary>쪼개면 반토막 통나무가 생기는 통나무(TreeLog.m_subLogPrefab): 반토막이 그 자리에 생겨 구른다.</summary>
        public bool Splits;
        /// <summary>여러 물건 중 무작위로 떨구는 통·궤짝(barrell 등, 부수는 것에 물건 3종 이상). 사냥감이 있는 물건은 이걸 얻는 법으로 안 친다(사슴 가죽).</summary>
        public bool Loot;
        /// <summary>땅에 묻혀 숨는 광맥(Beacon이 붙음 — 산 은). 묻혔으면 위시본을 얻어야 안다(HiddenOres, ⑪).</summary>
        public bool Hidden;
        /// <summary>프리팹 뿌리에서 맨 윗면까지 높이(m, 메시 기준). 드러났는지 볼 때 ZDO 높이에 더한다.</summary>
        public float TopOffset;
    }

    /// <summary>
    /// 게임 데이터에서 읽은 "어디서 무엇이 나오는지" (구상서 7-1장: 바이옴마다 손으로 넣지 않는다).
    /// 모드가 다 알아도 NPC는 본 것만 쓴다(Knowledge) — 이 목록은 "본 것이 무엇인지" 알아보는 데만 쓴다.
    /// </summary>
    public static class ResourceCatalog
    {
        static Dictionary<int, ResourceInfo> s_byHash;
        /// <summary>연료로 제련해 얻는 주괴(구리·주석·철 …). 철 주괴(Iron)는 어딘가에서 떨어지기도 하지만 NPC는 제련 계획으로 만든다.</summary>
        static readonly HashSet<string> s_smelted = new HashSet<string>();

        public static ResourceInfo Get(int prefabHash)
        {
            if (s_byHash == null) Build();
            return s_byHash.TryGetValue(prefabHash, out var r) ? r : null;
        }

        /// <summary>그 물건을 얻는 데 쓰는 도구(목록에서 처음 나오는 것). 캐는 물건이 아니면 None.</summary>
        /// <summary>그 물건을 얻는 방법들(맨손 줍기 = None, 도끼, 곡괭이). 비어 있으면 캐거나 줍는 물건이 아니다.</summary>
        public static HashSet<ToolKind> WaysFor(string item)
        {
            if (s_byHash == null) Build();
            var ways = new HashSet<ToolKind>();
            if (s_smelted.Contains(item)) return ways;
            bool prey = Prey.Any(item);
            foreach (var r in s_byHash.Values)
                if (r.Produces.Contains(item) && !(prey && r.Loot)) ways.Add(r.Tool);
            if (ways.Count == 0 && prey) ways.Add(ToolKind.Hunt);
            return ways;
        }

        /// <summary>그 물건이 나오는 캐는·줍는 것의 프리팹 이름(진단용).</summary>
        public static List<string> SourcesOf(string item)
        {
            if (s_byHash == null) Build();
            var list = new List<string>();
            foreach (var r in s_byHash.Values) if (r.Produces.Contains(item)) list.Add($"{r.Prefab}({r.Tool})");
            return list;
        }

        /// <summary>그 물건이 skip(예: 밭 작물 프리팹)이 아닌 캐는·줍는 것에서도 나오는지.</summary>
        public static bool AnySourceBut(string item, System.Func<string, bool> skip)
        {
            if (s_byHash == null) Build();
            foreach (var r in s_byHash.Values) if (r.Produces.Contains(item) && !skip(r.Prefab)) return true;
            return false;
        }

        /// <summary>그 물건이 묻혀 숨는 광맥(산 은)에서 나오는지. 은은 드러난 바위(rock3_silver)·깨진 조각에서도 나오지만 대부분 묻혀 있다.</summary>
        public static bool FromHidden(string item)
        {
            if (s_byHash == null) Build();
            foreach (var r in s_byHash.Values) if (r.Hidden && r.Produces.Contains(item)) return true;
            return false;
        }

        /// <summary>그 도구로 그 물건을 캐는 곳 중 가장 낮은 도구 단계(m_minToolTier). 그 도구로 캐는 곳이 없으면 0.</summary>
        public static int MinTier(string item, ToolKind tool)
        {
            if (s_byHash == null) Build();
            int min = int.MaxValue;
            foreach (var r in s_byHash.Values)
                if (r.Tool == tool && r.Produces.Contains(item) && r.MinToolTier < min) min = r.MinToolTier;
            return min == int.MaxValue ? 0 : min;
        }

        /// <summary>캐거나 줍는 방법이 하나라도 있는가(사냥만 되는 물건은 false).</summary>
        public static bool Gatherable(string item)
        {
            foreach (var w in WaysFor(item)) if (w != ToolKind.Hunt) return true;
            return false;
        }

        public static int Count { get { if (s_byHash == null) Build(); return s_byHash.Count; } }

        /// <summary>
        /// 땅을 파서라도 꺼낼 물건인지. 손으로 줍는 곳에서도 나오는 흔한 것(돌·부싯돌)은 땅속 조각을 남겨 두고(파지 않음),
        /// 구리처럼 광맥에서만 나오는 것은 판다(2026-10-21 사용자: 돌 캘 땐 땅 파기 최대한 자제).
        /// </summary>
        public static bool WorthDigging(string item)
        {
            if (s_byHash == null) Build();
            foreach (var r in s_byHash.Values)
                if (r.Pickable && r.Produces.Contains(item)) return false;
            return true;
        }

        static void Build()
        {
            s_byHash = new Dictionary<int, ResourceInfo>();
            s_smelted.Clear();
            if (ZNetScene.instance == null) return;
            // 그루터기는 Destructible이라 어린 나무와 구별이 안 된다 → 나무의 m_stubPrefab으로 알아낸다.
            var stubs = new HashSet<string>();
            // 숨는 광맥이 깨져 생긴 조각(silvervein_frac 등)은 누가 이미 깨기 시작한 것 = 드러난 것 → 숨지 않는다.
            var broken = new HashSet<string>();
            foreach (var p in ZNetScene.instance.m_prefabs)
            {
                var tree = p != null ? p.GetComponent<TreeBase>() : null;
                if (tree != null && tree.m_stubPrefab != null) stubs.Add(tree.m_stubPrefab.name);
                var des = p != null ? p.GetComponent<Destructible>() : null;
                if (des != null && des.m_spawnWhenDestroyed != null) broken.Add(des.m_spawnWhenDestroyed.name);
                var sm = p != null ? p.GetComponent<Smelter>() : null;
                if (sm != null && sm.m_fuelItem != null)
                    foreach (var c in sm.m_conversion)
                        if (c.m_to != null) s_smelted.Add(Utils.GetPrefabName(c.m_to.gameObject));
            }
            foreach (var p in ZNetScene.instance.m_prefabs)
            {
                if (p == null) continue;
                var info = Describe(p, 0);
                if (info == null || info.Produces.Count == 0) continue;
                if (stubs.Contains(p.name)) info.Priority = 0f;
                info.Hidden = !broken.Contains(p.name) && p.GetComponentInChildren<Beacon>(true) != null;
                if (info.Hidden) info.TopOffset = HiddenOres.TopOffset(p);
                s_byHash[p.name.GetStableHashCode()] = info;
            }
            Plugin.Log.LogInfo($"자원 목록: {s_byHash.Count}종");
        }

        /// <summary>월드가 바뀌면(ZNetScene 새로 생김) 다시 만든다.</summary>
        public static void Reset() { s_byHash = null; Prey.Reset(); HiddenOres.Reset(); }

        static ResourceInfo Describe(GameObject p, int depth)
        {
            if (p == null || depth > 4) return null;
            var tree = p.GetComponent<TreeBase>();
            if (tree != null)
            {
                var r = new ResourceInfo { Prefab = p.name, Tool = ToolKind.Axe, MinToolTier = tree.m_minToolTier, Priority = 10f, Falls = tree.m_logPrefab != null };
                AddDrops(r, tree.m_dropWhenDestroyed);
                AddFrom(r, Describe(tree.m_logPrefab, depth + 1));
                return r;
            }
            var log = p.GetComponent<TreeLog>();
            if (log != null)
            {
                var r = new ResourceInfo { Prefab = p.name, Tool = ToolKind.Axe, MinToolTier = log.m_minToolTier, Priority = 30f, Log = true, Splits = log.m_subLogPrefab != null };
                AddDrops(r, log.m_dropWhenDestroyed);
                AddFrom(r, Describe(log.m_subLogPrefab, depth + 1));
                return r;
            }
            var rock = p.GetComponent<MineRock>();
            if (rock != null)
            {
                var r = new ResourceInfo { Prefab = p.name, Tool = ToolKind.Pickaxe, MinToolTier = rock.m_minToolTier, Priority = 10f };
                AddDrops(r, rock.m_dropItems);
                return r;
            }
            var rock5 = p.GetComponent<MineRock5>();
            if (rock5 != null)
            {
                var r = new ResourceInfo { Prefab = p.name, Tool = ToolKind.Pickaxe, MinToolTier = rock5.m_minToolTier, Priority = 10f };
                AddDrops(r, rock5.m_dropItems);
                return r;
            }
            var des = p.GetComponent<Destructible>();
            if (des != null)
            {
                bool tree2 = (des.m_destructibleType & DestructibleType.Tree) != 0;
                var r = new ResourceInfo { Prefab = p.name, Tool = tree2 ? ToolKind.Axe : ToolKind.Pickaxe, MinToolTier = des.m_minToolTier };
                var drop = p.GetComponent<DropOnDestroyed>();
                if (drop != null) AddDrops(r, drop.m_dropWhenDestroyed);
                r.Loot = !tree2 && drop?.m_dropWhenDestroyed?.m_drops != null && drop.m_dropWhenDestroyed.m_drops.Count >= 3;
                AddFrom(r, Describe(des.m_spawnWhenDestroyed, depth + 1));
                return r;
            }
            var pick = p.GetComponent<Pickable>();
            if (pick != null && pick.m_itemPrefab != null)
            {
                var r = new ResourceInfo { Prefab = p.name, Tool = ToolKind.None, Pickable = true, Priority = 5f };
                if (pick.m_itemPrefab.GetComponent<ItemDrop>() != null) r.Produces.Add(pick.m_itemPrefab.name);
                AddDrops(r, pick.m_extraDrops);
                return r;
            }
            return null;
        }

        static void AddDrops(ResourceInfo r, DropTable table)
        {
            if (table?.m_drops == null) return;
            foreach (var d in table.m_drops)
                if (d.m_item != null && d.m_item.GetComponent<ItemDrop>() != null) r.Produces.Add(d.m_item.name);
        }

        static void AddFrom(ResourceInfo r, ResourceInfo sub)
        {
            if (sub == null) return;
            foreach (var item in sub.Produces) r.Produces.Add(item);
        }
    }
}
