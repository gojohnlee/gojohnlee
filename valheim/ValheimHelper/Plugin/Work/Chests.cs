using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// NPC가 쓰는 상자 (구상서 3장, 4장, 5장 !상자): 거점 깃발 20m 안의 상자만 쓴다.
    /// 지정은 NPC ZDO에 상자 ZDOID로 적는다(두 NPC 모두에). 장비 상자는 각자, 음식·씨앗·자원 상자는 같이 쓴다.
    /// 상자를 고칠 때는 누가 열고 있지 않을 때만, 소유권을 가져와 한 프레임 안에 읽고-고치고-저장한다(물건 복사·증발 방지).
    /// </summary>
    public static class Chests
    {
        public const float BaseRange = 20f;

        public static string KeyEquip(int role) => "vh_chest_equip_" + role;
        public const string KeyFood = "vh_chest_food";
        public const string KeySeeds = "vh_chest_seeds";
        public static string KeyItem(string prefab) => "vh_chest_item_" + prefab;

        public static void Assign(string key, ZDOID chest)
        {
            foreach (var zdo in Workers.Zdos()) zdo.Set(key, chest);
        }

        public static Container Assigned(string key)
        {
            foreach (var zdo in Workers.Zdos())
            {
                ZDOID id = zdo.GetZDOID(key);
                if (id.IsNone()) continue;
                GameObject go = ZNetScene.instance.FindInstance(id);
                if (go != null) return go.GetComponent<Container>();
            }
            return null;
        }

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

        /// <summary>그 물건을 넣을 상자: 그 물건으로 지정된 상자 → 이미 같은 게 있는 상자 → 빈자리 있는 상자(장비·음식·씨앗 상자 제외).</summary>
        public static Container ForItem(string prefab, Vector3 home)
        {
            Container assigned = Assigned(KeyItem(prefab));
            if (assigned != null) return assigned;
            GameObject go = ObjectDB.instance.GetItemPrefab(prefab);
            if (go == null) return null;
            ItemDrop.ItemData sample = go.GetComponent<ItemDrop>().m_itemData;
            Container fallback = null;
            foreach (var c in NearBase(home))
            {
                if (IsSpecial(c)) continue;
                if (!c.GetInventory().CanAddItem(sample, 1)) continue;
                if (c.GetInventory().HaveItem(sample.m_shared.m_name)) return c;
                if (fallback == null) fallback = c;
            }
            return fallback;
        }

        /// <summary>장비·음식·씨앗 상자로 지정된 상자인지(자원은 넣지 않는다).</summary>
        public static bool IsSpecial(Container c)
        {
            ZDOID id = c.GetComponent<ZNetView>().GetZDO().m_uid;
            foreach (var key in new[] { KeyEquip(0), KeyEquip(1), KeyFood, KeySeeds })
            {
                Container a = Assigned(key);
                if (a != null && a.GetComponent<ZNetView>().GetZDO().m_uid == id) return true;
            }
            return false;
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
