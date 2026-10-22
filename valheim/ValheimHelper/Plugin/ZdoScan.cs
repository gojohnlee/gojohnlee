using System.Collections.Generic;

namespace ValheimHelper
{
    /// <summary>
    /// 한 프리팹의 ZDO 전부(중복 없이). 바닐라 ZDOMan.GetAllZDOsWithPrefabIterative는 한 번에 400칸 넘게 보면 끊을 때
    /// index++ 전에 멈춰 그 칸을 다음 호출에서 또 읽고, 포탈은 지난 ZDO가 m_portalObjects에 남는 수도 있어 같은 ZDO가 두 번 나올 수 있다.
    /// </summary>
    static class ZdoScan
    {
        public static List<ZDO> All(string prefab)
        {
            var list = new List<ZDO>();
            int index = 0;
            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(prefab, list, ref index)) { }
            var seen = new HashSet<ZDOID>();
            list.RemoveAll(z => z == null || !z.IsValid() || !seen.Add(z.m_uid));
            return list;
        }
    }
}
