using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// NPC가 쓰는 상자 (구상서 3장, 4장): 거점 깃발 20m 안의 상자 전부. 지정하지 않는다(사용자 결정 2026-10-21 "상자는 지정하지 않는다").
    /// 장비·음식·미드는 거점 상자 전부에서 찾고, 넣기는 같은 물건 있는 상자 → 빈자리 있는 상자(장비·자원 구분 없음).
    /// 상자를 고칠 때는 누가 열고 있지 않을 때만, 소유권을 가져와 한 프레임 안에 읽고-고치고-저장한다(물건 복사·증발 방지).
    /// </summary>
    public static class Chests
    {
        public const float BaseRange = 20f;

        public static Container FromId(ZDOID id)
        {
            if (id.IsNone()) return null;
            GameObject go = ZNetScene.instance.FindInstance(id);
            return go != null ? go.GetComponentInParent<Container>() : null;
        }

        /// <summary>거점 근처 상자들(건축물인 것만: 수레·배 제외), 가까운 순.</summary>
        public static List<Container> NearBase(Vector3 home)
        {
            var list = new List<Container>();
            foreach (var zdo in Senses.Nearby(home, BaseRange))
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
        public static Container ForItem(string prefab, Vector3 home)
        {
            GameObject go = ObjectDB.instance.GetItemPrefab(prefab);
            if (go == null) return null;
            ItemDrop.ItemData sample = go.GetComponent<ItemDrop>().m_itemData;
            Container fallback = null;
            foreach (var c in NearBase(home))
            {
                if (!c.GetInventory().CanAddItem(sample, 1)) continue;
                if (c.GetInventory().HaveItem(sample.m_shared.m_name)) return c;
                if (fallback == null) fallback = c;
            }
            return fallback;
        }

        /// <summary>거점 상자 전부에서 그 이름의 물건 수.</summary>
        public static int CountAll(Vector3 home, Func<Inventory, int> count)
        {
            int n = 0;
            foreach (var c in NearBase(home)) n += count(c.GetInventory());
            return n;
        }

        /// <summary>조건에 맞는 첫 거점 상자(가까운 순), 없으면 null.</summary>
        public static Container First(Vector3 home, Func<Inventory, bool> match)
        {
            foreach (var c in NearBase(home)) if (match(c.GetInventory())) return c;
            return null;
        }

        static readonly Func<Container, bool> LoadContainer =
            AccessTools.MethodDelegate<Func<Container, bool>>(AccessTools.Method(typeof(Container), "Load"));

        public static bool InUse(Container c)
        {
            ZNetView view = c.GetComponent<ZNetView>();
            return view == null || !view.IsValid() || c.IsInUse() || view.GetZDO().GetInt(ZDOVars.s_inUse) == 1;
        }

        /// <summary>상자 내용을 고친다. 누가 열고 있으면 false(나중에 다시).</summary>
        public static bool TryEdit(Container c, Action<Inventory> edit)
        {
            if (c == null || InUse(c)) return false;
            ZNetView view = c.GetComponent<ZNetView>();
            view.GetZDO().SetOwner(ZDOMan.GetSessionID());
            LoadContainer(c); // 다른 사람이 마지막으로 저장한 내용을 읽는다(이미 최신이면 그대로)
            edit(c.GetInventory()); // 바뀌면 Container.OnContainerChanged가 저장한다(소유자이므로)
            return true;
        }
    }
}
