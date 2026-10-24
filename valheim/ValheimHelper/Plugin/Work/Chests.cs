using System;
using System.Collections.Generic;
using HarmonyLib;
using ValheimHelper.Core.Work;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// NPC가 쓰는 상자 (구상서 3장, 4장): 거점 깃발 125m(BaseArea) 안의 상자 전부. 지정하지 않는다(사용자 결정 2026-10-21 "상자는 지정하지 않는다").
    /// 장비·음식·미드는 거점 상자 전부에서 찾고, 넣기는 같은 물건 있는 상자 → 빈자리 있는 상자(장비·자원 구분 없음).
    /// 상자를 고칠 때는 누가 열고 있지 않을 때만, 소유권을 가져와 한 프레임 안에 읽고-고치고-저장한다(물건 복사·증발 방지).
    /// </summary>
    /// <summary>상자 역할(I-1): 장비·음식/미드·재료. Any = 역할 구분 없이 거점 상자 전부.</summary>
    public enum ChestRole { Any, Gear, Food, Mats }

    public static class Chests
    {
        public const float BaseRange = BaseArea.Radius;

        /// <summary>태그 공급자(I-2): 그 상자(ZDO)의 이름이 가리키는 것. null = 태그 없음 = 거점 상자 전부. 기본은 상자 이름 읽기.</summary>
        public static Func<ZDO, ChestTags> TagProvider = ChestTagReader.Read;

        static readonly Dictionary<int, bool> s_chestPrefab = new Dictionary<int, bool>();

        /// <summary>건축물 상자 프리팹인가(Container·Piece·WearNTear — NearBase·ReadAll과 같은 조건, 수레·배 제외). 프리팹마다 한 번만 봄.</summary>
        internal static bool IsChest(ZDO zdo)
        {
            int hash = zdo.GetPrefab();
            if (s_chestPrefab.TryGetValue(hash, out bool yes)) return yes;
            GameObject p = ZNetScene.instance.GetPrefab(hash);
            yes = p != null && p.GetComponent<Container>() != null && p.GetComponent<Piece>() != null && p.GetComponent<WearNTear>() != null;
            if (p != null) s_chestPrefab[hash] = yes; // 못 찾은 해시는 기억하지 않음
            return yes;
        }

        /// <summary>
        /// 거점 상자 후보: 건축물 상자이고 지하묘지 안(y+5000, InInterior)이 아님. 거점 둘레(xz 125m) 안에 지하묘지가 있으면 그 보물 상자
        /// (TreasureChest_sunkencrypt — Piece·WearNTear 있음)가 거점 상자로 잡혀 재고로 세고 캔 고철을 거기 넣었다(회차 136 vh_crypttest).
        /// </summary>
        internal static bool IsBaseChest(ZDO zdo) => IsChest(zdo) && !Character.InInterior(zdo.GetPosition());

        /// <summary>그 역할에 쓸 거점 근처 ZDO들(이름 규칙은 Core ChestNames: 역할 상자가 있으면 그것만, 손대지 않는 상자는 항상 뺌).</summary>
        static List<ZDO> Candidates(Vector3 home, ChestRole role)
        {
            // 상자 프리팹만(나무·바위 등 수천 개는 이름 읽기 전에 뺌 — 회차 109 부하 재기: 한 번 0.8ms → 짐 정리 최악 50ms).
            // 상자 아닌 것(이름 없음 = 일반)이 섞이면 Select가 "일반 상자 있음"으로 잘못 봐 역할 상자만 있을 때 빈 목록이 됨 — 그것도 막음.
            List<ZDO> all = Senses.Nearby(home, BaseRange, IsBaseChest);
            var provider = TagProvider;
            if (provider == null) return all;
            var tags = new ChestTags[all.Count];
            for (int i = 0; i < tags.Length; i++) tags[i] = provider(all[i]);
            ChestTags want = role == ChestRole.Gear ? ChestTags.Gear : role == ChestRole.Food ? ChestTags.Food : role == ChestRole.Mats ? ChestTags.Mats : ChestTags.None;
            bool[] ok = ChestNames.Select(tags, want);
            var list = new List<ZDO>();
            for (int i = 0; i < ok.Length; i++) if (ok[i]) list.Add(all[i]);
            return list;
        }

        /// <summary>물건 종류에 맞는 상자 역할: 먹는 것 → 음식, 무기·방패·갑옷·도구 → 장비, 나머지 → 재료.</summary>
        public static ChestRole RoleFor(ItemDrop.ItemData it)
        {
            switch (it.m_shared.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.Consumable: return ChestRole.Food;
                case ItemDrop.ItemData.ItemType.OneHandedWeapon: case ItemDrop.ItemData.ItemType.TwoHandedWeapon: case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                case ItemDrop.ItemData.ItemType.Bow: case ItemDrop.ItemData.ItemType.Shield: case ItemDrop.ItemData.ItemType.Tool: case ItemDrop.ItemData.ItemType.Torch:
                case ItemDrop.ItemData.ItemType.Helmet: case ItemDrop.ItemData.ItemType.Chest: case ItemDrop.ItemData.ItemType.Legs: case ItemDrop.ItemData.ItemType.Shoulder:
                case ItemDrop.ItemData.ItemType.Utility: case ItemDrop.ItemData.ItemType.Ammo:
                    return ChestRole.Gear;
                default: return ChestRole.Mats;
            }
        }

        public static Container FromId(ZDOID id)
        {
            if (id.IsNone()) return null;
            GameObject go = ZNetScene.instance.FindInstance(id);
            return go != null ? go.GetComponentInParent<Container>() : null;
        }

        /// <summary>거점 근처 상자들(건축물인 것만: 수레·배 제외), 가까운 순.</summary>
        public static List<Container> NearBase(Vector3 home, ChestRole role = ChestRole.Any)
        {
            var list = new List<Container>();
            foreach (var zdo in Candidates(home, role))
            {
                ZNetView view = ZNetScene.instance.FindInstance(zdo);
                if (view == null) continue;
                Container c = view.GetComponent<Container>();
                if (c != null && view.GetComponent<Piece>() != null && view.GetComponent<WearNTear>() != null) list.Add(c);
            }
            list.Sort((a, b) => Utils.DistanceXZ(a.transform.position, home).CompareTo(Utils.DistanceXZ(b.transform.position, home)));
            return list;
        }

        /// <summary>그 물건을 넣을 상자: 이미 같은 게 있는 상자 → 빈자리 있는 상자(가까운 것부터).</summary>
        public static Container ForItem(string prefab, Vector3 home, ChestRole role = ChestRole.Any)
        {
            GameObject go = ObjectDB.instance.GetItemPrefab(prefab);
            if (go == null) return null;
            ItemDrop.ItemData sample = go.GetComponent<ItemDrop>().m_itemData;
            Container fallback = null;
            foreach (var c in NearBase(home, role))
            {
                if (!c.GetInventory().CanAddItem(sample, 1)) continue;
                if (c.GetInventory().HaveItem(sample.m_shared.m_name)) return c;
                if (fallback == null) fallback = c;
            }
            return fallback;
        }

        /// <summary>
        /// 읽기 전용: 거점 상자 인벤토리 전부. 거점 구역이 안 불러와져 인스턴스가 없는 상자는 ZDO(s_items)에서 읽는다
        /// (호스트·NPC가 둘 다 거점에서 멀 때 재고가 0으로 보이던 것, 손님 코드 검사 B). 고치기는 TryEdit(인스턴스)로만.
        /// </summary>
        static IEnumerable<Inventory> ReadAll(Vector3 home, ChestRole role)
        {
            foreach (var zdo in Candidates(home, role))
            {
                ZNetView view = ZNetScene.instance.FindInstance(zdo);
                if (view != null)
                {
                    Container c = view.GetComponent<Container>();
                    if (c != null && view.GetComponent<Piece>() != null && view.GetComponent<WearNTear>() != null) yield return c.GetInventory();
                    continue;
                }
                GameObject prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
                if (prefab == null || prefab.GetComponent<Piece>() == null || prefab.GetComponent<WearNTear>() == null) continue;
                Container proto = prefab.GetComponent<Container>();
                if (proto == null) continue;
                var inv = new Inventory(proto.m_name, null, proto.m_width, proto.m_height);
                byte[] data = zdo.GetByteArray(ZDOVars.s_items);
                if (data != null) inv.Load(new ZPackage(data)); // 위치 검사 없이 넣으므로 크기는 상관없음
                yield return inv;
            }
        }

        /// <summary>거점 상자 전부에서 그 이름의 물건 수.</summary>
        public static int CountAll(Vector3 home, Func<Inventory, int> count, ChestRole role = ChestRole.Any)
        {
            int n = 0;
            foreach (var inv in ReadAll(home, role)) n += count(inv);
            return n;
        }

        /// <summary>거점 상자 중 조건에 맞는 게 있나(안 불러와진 상자 포함).</summary>
        public static bool Any(Vector3 home, Func<Inventory, bool> match, ChestRole role = ChestRole.Any)
        {
            foreach (var inv in ReadAll(home, role)) if (match(inv)) return true;
            return false;
        }

        /// <summary>조건에 맞는 첫 거점 상자(가까운 순), 없으면 null.</summary>
        public static Container First(Vector3 home, Func<Inventory, bool> match, ChestRole role = ChestRole.Any)
        {
            foreach (var c in NearBase(home, role)) if (match(c.GetInventory())) return c;
            return null;
        }

        static readonly Func<Container, bool> LoadContainer =
            AccessTools.MethodDelegate<Func<Container, bool>>(AccessTools.Method(typeof(Container), "Load"));

        public static bool InUse(Container c)
        {
            ZNetView view = c.GetComponent<ZNetView>();
            return view == null || !view.IsValid() || c.IsInUse() || view.GetZDO().GetInt(ZDOVars.s_inUse) == 1 || SharedChests.GuestViewing(view.GetZDO().m_uid);
        }

        /// <summary>상자 내용을 고친다. 누가 열고 있으면 false(나중에 다시).</summary>
        public static bool TryEdit(Container c, Action<Inventory> edit)
        {
            if (c == null || InUse(c)) return false;
            ZNetView view = c.GetComponent<ZNetView>();
            view.GetZDO().SetOwner(ZDOMan.GetSessionID());
            LoadContainer(c); // 다른 사람이 마지막으로 저장한 내용을 읽는다(이미 최신이면 그대로)
            Inventory inv = c.GetInventory();
            if (s_removed.Count == 0) { edit(inv); return true; } // 바뀌면 Container.OnContainerChanged가 저장한다(소유자이므로)
            var names = new List<string>(s_removed.Keys);
            var before = new int[names.Count];
            for (int i = 0; i < names.Count; i++) before[i] = inv.CountItems(names[i]);
            edit(inv);
            for (int i = 0; i < names.Count; i++)
            {
                int gone = before[i] - inv.CountItems(names[i]);
                if (gone > 0) s_removed[names[i]] += gone;
            }
            return true;
        }

        /// <summary>
        /// E001 소실 보충용: 지켜보는 물건(공유 이름)이 모드 편집(TryEdit)으로 상자에서 빠진 누적 수 — 정리하는 동안 다른 일꾼이 가져간 것을
        /// 소실로 잘못 보지 않게(보충 = 복제가 되므로). 한 번 지켜보면 계속(설비 결과물 몇 가지뿐).
        /// </summary>
        static readonly Dictionary<string, int> s_removed = new Dictionary<string, int>();
        public static void Watch(string name) { if (name != null && !s_removed.ContainsKey(name)) s_removed[name] = 0; }
        public static int Removed(string name) => name != null && s_removed.TryGetValue(name, out int n) ? n : 0;
    }
}
