using System.Collections.Generic;
using ValheimHelper.Core.Work;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 우리 = 가축 상자(ChestTags.Livestock) 둘레 LivestockJob.PenRange. 우리마다 받는 종(사용자 11-10 가축 4):
    /// 상자 이름이 종 이름이면 그 종만(Core ChestNames.PenSpecies), "가축"이면 이미 들어온 길든 것 가운데 가장 많은 종(맨 처음 들어온 종으로 굳음), 비었으면 아무 종.
    /// 따로 저장하지 않음(우리 안 짐승이 곧 기록).
    /// </summary>
    internal static class Pens
    {
        /// <summary>집 기준 거점 안 가축 상자들.</summary>
        internal static List<Container> Near(Vector3 home)
        {
            var list = new List<Container>();
            var provider = Chests.TagProvider;
            if (provider == null) return list;
            foreach (var zdo in Chests.BaseChestZdos(home))
            {
                if ((provider(zdo) & ChestTags.Livestock) == 0) continue;
                ZNetView view = ZNetScene.instance.FindInstance(zdo);
                Container c = view != null ? view.GetComponent<Container>() : null;
                if (c != null) list.Add(c);
            }
            return list;
        }

        /// <summary>그 우리가 받는 종(프리팹 이름, CullJob.Species 꼴). null = 아무 종.</summary>
        internal static string SpeciesOf(Container pen)
        {
            ZNetView v = pen.GetComponent<ZNetView>();
            string named = v != null && v.GetZDO() != null ? ChestNames.PenSpecies(ChestTagReader.Name(v.GetZDO())) : null;
            if (named != null) return named;
            var count = new Dictionary<string, int>();
            string best = null;
            foreach (var a in Character.GetAllCharacters())
            {
                if (a == null || a.IsDead() || !a.IsTamed() || a is WorkerNpc || a.IsPlayer() || !LivestockJob.InPen(a, pen.transform.position)) continue;
                string s = CullJob.Species(a);
                count.TryGetValue(s, out int n);
                count[s] = ++n;
                if (best == null || n > count[best]) best = s;
            }
            return best;
        }

        /// <summary>우리마다 받는 종(한 번 계산해 여러 짐승에 씀).</summary>
        internal static Dictionary<Container, string> SpeciesMap(List<Container> pens)
        {
            var map = new Dictionary<Container, string>();
            foreach (var p in pens) map[p] = SpeciesOf(p);
            return map;
        }

        internal static bool Accepts(Dictionary<Container, string> map, Container pen, Character a)
            => !map.TryGetValue(pen, out string s) || s == null || s == CullJob.Species(a);
    }
}
