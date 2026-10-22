using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// 땅에 묻혀 숨는 광맥(⑪ 산 은, ResourceInfo.Hidden = 프리팹에 Beacon): 드러났으면 늘 알고 먼저 캐고,
    /// 묻혔으면 플레이어가 찾는 물건(위시본 = 장착 효과가 SE_Finder인 물건)을 얻었을 때만 안다(Core HiddenOre, 사용자 결정 2026-10-22).
    /// </summary>
    public static class HiddenOres
    {
        static ItemDrop s_finder;
        static bool s_finderLooked;

        /// <summary>찾는 물건(위시본). 게임 데이터에서 장착 효과가 SE_Finder인 첫 물건, 없으면 null(그땐 묻힌 것도 안다 — Unlocks.Have(null)).</summary>
        public static ItemDrop Finder
        {
            get
            {
                if (s_finderLooked || ObjectDB.instance == null) return s_finder;
                s_finderLooked = true;
                foreach (GameObject go in ObjectDB.instance.m_items)
                {
                    var drop = go != null ? go.GetComponent<ItemDrop>() : null;
                    if (drop != null && drop.m_itemData.m_shared.m_equipStatusEffect is SE_Finder) { s_finder = drop; break; }
                }
                return s_finder;
            }
        }

        public static void Reset() { s_finder = null; s_finderLooked = false; }

        public static bool HaveFinder() => Unlocks.Have(Finder);

        /// <summary>그 ZDO의 광맥이 드러났나: 윗면(ZDO 높이 + TopOffset)이 지금 땅(파인 땅 포함, 안 불러온 구역은 원래 땅)보다 나왔다.</summary>
        public static bool Exposed(ZDO zdo, ResourceInfo info)
        {
            Vector3 p = zdo.GetPosition();
            float ground = ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(p, out float h) ? h
                : WorldGenerator.instance != null ? WorldGenerator.instance.GetHeight(p.x, p.z) : p.y;
            return HiddenOre.Exposed(p.y + info.TopOffset, ground);
        }

        /// <summary>일꾼이 이 자원을 아는가(숨는 광맥이 아니면 늘 true).</summary>
        public static bool Known(ZDO zdo, ResourceInfo info) =>
            !info.Hidden || HiddenOre.Known(true, Exposed(zdo, info), HaveFinder());

        /// <summary>프리팹 뿌리에서 맨 윗면까지(메시 경계 기준, 프리팹 크기 그대로 — 심을 때 크기가 바뀐 것은 셈 안 함). 메시가 없으면 0.</summary>
        public static float TopOffset(GameObject prefab)
        {
            float top = float.MinValue;
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Bounds b = mf.sharedMesh.bounds;
                Matrix4x4 m = mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    top = Mathf.Max(top, m.MultiplyPoint3x4(c).y - prefab.transform.position.y);
                }
            }
            return top == float.MinValue ? 0f : top;
        }
    }
}
