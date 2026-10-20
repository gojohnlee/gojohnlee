using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    public enum ToolKind { None, Axe, Pickaxe }

    /// <summary>캘 수 있는 것 하나(나무, 통나무, 그루터기, 바위 …)의 바닐라 정보.</summary>
    public sealed class ResourceInfo
    {
        public string Prefab;
        public ToolKind Tool;
        public int MinToolTier;
        /// <summary>쓰러뜨리면(쪼개면) 결국 나오는 물건 프리팹 이름들.</summary>
        public readonly HashSet<string> Produces = new HashSet<string>();
        /// <summary>이미 쓰러진 것(통나무·그루터기): 서 있는 나무보다 먼저 처리한다.</summary>
        public bool Felled;
    }

    /// <summary>
    /// 게임 데이터에서 읽은 "어디서 무엇이 나오는지" (구상서 7-1장: 바이옴마다 손으로 넣지 않는다).
    /// 모드가 다 알아도 NPC는 본 것만 쓴다(Knowledge) — 이 목록은 "본 것이 무엇인지" 알아보는 데만 쓴다.
    /// </summary>
    public static class ResourceCatalog
    {
        static Dictionary<int, ResourceInfo> s_byHash;

        public static ResourceInfo Get(int prefabHash)
        {
            if (s_byHash == null) Build();
            return s_byHash.TryGetValue(prefabHash, out var r) ? r : null;
        }

        /// <summary>그 물건을 얻는 데 쓰는 도구(목록에서 처음 나오는 것). 캐는 물건이 아니면 None.</summary>
        public static ToolKind ToolFor(string item)
        {
            if (s_byHash == null) Build();
            foreach (var r in s_byHash.Values)
                if (r.Produces.Contains(item)) return r.Tool;
            return ToolKind.None;
        }

        public static int Count { get { if (s_byHash == null) Build(); return s_byHash.Count; } }

        static void Build()
        {
            s_byHash = new Dictionary<int, ResourceInfo>();
            if (ZNetScene.instance == null) return;
            foreach (var p in ZNetScene.instance.m_prefabs)
            {
                if (p == null) continue;
                var info = Describe(p, 0);
                if (info != null && info.Produces.Count > 0) s_byHash[p.name.GetStableHashCode()] = info;
            }
            Plugin.Log.LogInfo($"자원 목록: {s_byHash.Count}종");
        }

        /// <summary>월드가 바뀌면(ZNetScene 새로 생김) 다시 만든다.</summary>
        public static void Reset() => s_byHash = null;

        static ResourceInfo Describe(GameObject p, int depth)
        {
            if (p == null || depth > 4) return null;
            var tree = p.GetComponent<TreeBase>();
            if (tree != null)
            {
                var r = new ResourceInfo { Prefab = p.name, Tool = ToolKind.Axe, MinToolTier = tree.m_minToolTier };
                AddDrops(r, tree.m_dropWhenDestroyed);
                AddFrom(r, Describe(tree.m_logPrefab, depth + 1));
                return r;
            }
            var log = p.GetComponent<TreeLog>();
            if (log != null)
            {
                var r = new ResourceInfo { Prefab = p.name, Tool = ToolKind.Axe, MinToolTier = log.m_minToolTier, Felled = true };
                AddDrops(r, log.m_dropWhenDestroyed);
                AddFrom(r, Describe(log.m_subLogPrefab, depth + 1));
                return r;
            }
            var rock = p.GetComponent<MineRock>();
            if (rock != null)
            {
                var r = new ResourceInfo { Prefab = p.name, Tool = ToolKind.Pickaxe, MinToolTier = rock.m_minToolTier };
                AddDrops(r, rock.m_dropItems);
                return r;
            }
            var rock5 = p.GetComponent<MineRock5>();
            if (rock5 != null)
            {
                var r = new ResourceInfo { Prefab = p.name, Tool = ToolKind.Pickaxe, MinToolTier = rock5.m_minToolTier };
                AddDrops(r, rock5.m_dropItems);
                return r;
            }
            var des = p.GetComponent<Destructible>();
            if (des != null)
            {
                bool tree2 = (des.m_destructibleType & DestructibleType.Tree) != 0;
                var r = new ResourceInfo { Prefab = p.name, Tool = tree2 ? ToolKind.Axe : ToolKind.Pickaxe, MinToolTier = des.m_minToolTier, Felled = tree2 };
                var drop = p.GetComponent<DropOnDestroyed>();
                if (drop != null) AddDrops(r, drop.m_dropWhenDestroyed);
                AddFrom(r, Describe(des.m_spawnWhenDestroyed, depth + 1));
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
