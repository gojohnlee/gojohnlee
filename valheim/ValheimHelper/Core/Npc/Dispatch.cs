using System;
using System.Collections.Generic;
using ValheimHelper.Core.Chat;

namespace ValheimHelper.Core.Npc
{
    public sealed class NpcInfo
    {
        public string Name;
        public Personality Personality;
        /// <summary>일을 하는 중(따라가기 포함)이면 true.</summary>
        public bool Busy;
    }

    /// <summary>명령을 받을 NPC 고르기 (구상서 3-2장).</summary>
    public static class Dispatch
    {
        /// <returns>npcs 안의 번호들. 비어 있으면 받을 NPC가 없다.</returns>
        public static List<int> Choose(CommandKind kind, string target, IReadOnlyList<NpcInfo> npcs)
        {
            var result = new List<int>();
            if (target != null)
            {
                for (int i = 0; i < npcs.Count; i++)
                    if (string.Equals(npcs[i].Name, target, StringComparison.OrdinalIgnoreCase)) { result.Add(i); break; }
                return result;
            }
            switch (kind)
            {
                // 둘 다 보여 주거나 둘 다 멈춘다.
                case CommandKind.Status:
                case CommandKind.Skills:
                case CommandKind.Stop:
                case CommandKind.ClearQueue:
                    for (int i = 0; i < npcs.Count; i++) result.Add(i);
                    return result;
            }
            // 나머지는 한 명: 한가한 쪽, 같으면 일의 성격(전투 위주는 알라라크형, 그 밖은 SCV형).
            Personality prefer = PrefersAlarak(kind) ? Personality.Alarak : Personality.Scv;
            int best = -1, bestScore = int.MinValue;
            for (int i = 0; i < npcs.Count; i++)
            {
                int score = (npcs[i].Busy ? 0 : 2) + (npcs[i].Personality == prefer ? 1 : 0);
                if (score > bestScore) { best = i; bestScore = score; }
            }
            if (best >= 0) result.Add(best);
            return result;
        }

        // 지금은 전투 위주 명령이 없다. 사냥·던전이 생기면 여기에 더한다.
        static bool PrefersAlarak(CommandKind kind) => false;
    }
}
