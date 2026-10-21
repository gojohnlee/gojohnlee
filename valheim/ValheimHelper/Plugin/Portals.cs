using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// 포탈 목록 읽기(설계 ⑤): 호스트가 ZDO에서 포탈과 짝 출구를 읽는다(구역이 안 불러와져도 됨).
    /// 바닐라 TeleportWorld.Teleport와 같은 출구: 짝 위치 + 짝 앞 m_exitDistance(1m) + 위로 1m.
    /// </summary>
    public static class Portals
    {
        public const string PrefabName = "portal_wood";
        const float ExitDistance = 1f;

        /// <summary>짝이 이어진 포탈만. 호스트(서버)에서만 부른다.</summary>
        public static List<PortalLink> Linked()
        {
            var result = new List<PortalLink>();
            if (ZDOMan.instance == null || ZNet.instance == null || !ZNet.instance.IsServer()) return result;
            var list = new List<ZDO>();
            int index = 0;
            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(PrefabName, list, ref index)) { }
            foreach (var zdo in list)
            {
                ZDOID otherId = zdo.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal);
                if (otherId == ZDOID.None) continue;
                ZDO other = ZDOMan.instance.GetZDO(otherId);
                if (other == null) continue;
                Vector3 p = zdo.GetPosition();
                Quaternion rot = other.GetRotation();
                Vector3 exit = other.GetPosition() + rot * Vector3.forward * ExitDistance + Vector3.up;
                result.Add(new PortalLink { X = p.x, Y = p.y, Z = p.z, ExitX = exit.x, ExitY = exit.y, ExitZ = exit.z });
            }
            return result;
        }

        /// <summary>from에서 to로 갈 때 탈 포탈(없으면 false: 걸어간다).</summary>
        public static bool TryChoose(Vector3 from, Vector3 to, out PortalLink link)
        {
            link = default;
            var all = Linked();
            int i = PortalRoute.Choose(from.x, from.z, to.x, to.z, all);
            if (i < 0) return false;
            link = all[i];
            return true;
        }
    }
}
