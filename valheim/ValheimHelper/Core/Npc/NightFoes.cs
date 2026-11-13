using System;
using System.Collections.Generic;

namespace ValheimHelper.Core.Npc
{
    /// <summary>밤에만 나는 생성 줄 하나(게임 SpawnData: 켜짐·그 바이옴·m_spawnAtDay 꺼짐): 종류와 밤에 함께 있을 수(m_maxSpawned).</summary>
    public sealed class NightSpawn
    {
        public string Kind;
        public int Count;
    }

    /// <summary>
    /// 밤 몫(사용자 11-13 17:30): 밤에만 더 나오는 적을 위험 판단 목록에 더한다. 장비와 무관한 고정 값이 아니라
    /// 그 바이옴 밤 생성 줄의 종류·수만 셈 → 센 장비면 밤 목초지도 안 위험으로 나온다.
    /// night가 비면 목록 그대로(사본).
    /// </summary>
    public static class NightFoes
    {
        public static List<Foe> Plus(IList<Foe> foes, IList<NightSpawn> night, Func<GroupFoe, Foe> resolve)
        {
            var list = new List<Foe>(foes);
            if (night == null || night.Count == 0) return list;
            // 같은 종류가 여러 줄이면 합이 아니라 가장 큰 수만: 게임은 줄마다 그 프리팹의 근처 수가 m_maxSpawned 이상이면 안 낳는다(SpawnSystem.UpdateSpawnList)
            var extra = new List<GroupFoe>(night.Count);
            foreach (NightSpawn s in night)
            {
                if (s.Count <= 0) continue;
                GroupFoe same = extra.Find(g => g.Kind == s.Kind);
                if (same == null) extra.Add(new GroupFoe { Kind = s.Kind, Count = s.Count });
                else same.Count = Math.Max(same.Count, s.Count);
            }
            list.AddRange(PlaceBook.Expand(extra, resolve));
            return list;
        }
    }
}
