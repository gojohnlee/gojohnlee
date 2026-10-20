using System.Collections.Generic;
using Verse;

namespace IronCavalry
{
    /// <summary>연속 스턴 방지 목록. 최대 수 초짜리라 저장하지 않는다(기획서 A9).</summary>
    public class GameComponent_IronCavalry : GameComponent
    {
        private readonly Dictionary<int, int> stunImmuneUntil = new Dictionary<int, int>();
        private readonly List<int> tmpExpired = new List<int>();

        public GameComponent_IronCavalry(Game game)
        {
        }

        public static GameComponent_IronCavalry Instance => Current.Game?.GetComponent<GameComponent_IronCavalry>();

        public bool IsStunImmune(Pawn pawn, int now)
        {
            return stunImmuneUntil.TryGetValue(pawn.thingIDNumber, out int until) && now < until;
        }

        public void SetStunImmuneUntil(Pawn pawn, int tick)
        {
            PruneExpired(Find.TickManager.TicksGame);
            stunImmuneUntil[pawn.thingIDNumber] = tick;
        }

        private void PruneExpired(int now)
        {
            if (stunImmuneUntil.Count < 32) return;
            tmpExpired.Clear();
            foreach (KeyValuePair<int, int> kv in stunImmuneUntil)
            {
                if (kv.Value <= now) tmpExpired.Add(kv.Key);
            }
            for (int i = 0; i < tmpExpired.Count; i++) stunImmuneUntil.Remove(tmpExpired[i]);
        }
    }
}
