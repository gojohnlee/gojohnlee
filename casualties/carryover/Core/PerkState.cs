using System;
using System.Globalization;
using System.Text;

namespace Carryover.Core
{
    /// <summary>퍽 하나: 단계마다 값(비용)이 오르고 상한이 있다.</summary>
    public sealed class PerkDef
    {
        public readonly string Id, Name, PerLevel;
        public readonly int[] Costs;   // Costs[i] = i+1단계로 올리는 값
        public int MaxLevel => Costs.Length;
        public PerkDef(string id, string name, string perLevel, params int[] costs) { Id = id; Name = name; PerLevel = perLevel; Costs = costs; }
    }

    /// <summary>
    /// 영구 퍽: 한 번 산 단계는 모든 회차에 계속 적용. 포인트는 셈하는 회차가 끝날 때(죽음·엔딩) 성과로 받고, 캐릭터 선택 화면에서 쓴다. 안 쓴 포인트는 남는다.
    /// 값 맞추기: 전부 사는 데 94점(아공간 10단계 40점), 한 회차 최대 6점(보통 2~4점) → 전부 사려면 대략 16~32회차.
    /// </summary>
    public sealed class PerkState
    {
        public static readonly PerkDef[] Perks =
        {
            new PerkDef("subspace", "아공간", "무게 없는 칸 +1 (한 칸에 같은 물건 999개까지, 다음 회차로 이어짐)", 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 6, 6, 7, 7, 7, 8, 8, 8, 9, 9),
            new PerkDef("lockpick", "자물쇠 달인", "자물쇠 따기 허용 각도 +25%", 1, 2, 3),
            new PerkDef("crafting", "숙련 제작", "제작할 때 지능 +1로 침 (실패·품질 저하 줄어듦)", 1, 2, 3),
            new PerkDef("traps", "함정 감지", "가까운 함정 표시 (반경 6 → 10 → 15)", 2, 3, 4),
            new PerkDef("xp", "경험치 증가", "경험치 획득 +10%", 2, 3, 4),
            new PerkDef("mining", "광부", "벽·바위를 칠 때 피해 +25%", 1, 2, 3),
            new PerkDef("fall", "튼튼한 다리", "떨어지거나 부딪힐 때 받는 충격 -20%", 2, 3, 4),
            new PerkDef("temp", "온도 적응력", "체온이 정상에서 멀어지는 속도 -20% (회복은 그대로)", 2, 3, 4),
        };

        public const float MinRunSeconds = 180f;   // 3분 안에 끝난 회차는 0점(바로 죽어 모으기 막기)

        public int Points;
        public readonly int[] Levels = new int[Perks.Length];
        /// <summary>지금 회차에서 이미 준 포인트(같은 회차 끝이 두 번 불려도 한 번만 주려고).</summary>
        public int RunAward;

        public static int IndexOf(string id) => Array.FindIndex(Perks, p => p.Id == id);
        public int Level(string id) => Levels[IndexOf(id)];

        /// <summary>다음 단계 값. 상한이면 -1.</summary>
        public int NextCost(int i) => Levels[i] >= Perks[i].MaxLevel ? -1 : Perks[i].Costs[Levels[i]];

        public bool CanBuy(int i) { int c = NextCost(i); return c > 0 && Points >= c; }

        public bool Buy(int i)
        {
            if (!CanBuy(i)) return false;
            Points -= NextCost(i);
            Levels[i]++;
            return true;
        }

        // ---- 효과 ----
        public int SubspaceSlots => Level("subspace");                  // 1~20칸(칸 = 물건 종류)
        public const int MaxStack = 999;
        /// <summary>아공간에 하나 더 넣을 수 있나: 같은 물건이 이미 있으면 그 칸에 999개까지, 새 종류면 칸이 남아야(사용자 11-22).</summary>
        public static bool SubspaceAccepts(int kindsNow, int sameIdNow, int capacity) => sameIdNow > 0 ? sameIdNow < MaxStack : kindsNow < capacity;
        public float LockpickMult => 1f + 0.25f * Level("lockpick");    // 3단계 ×1.75
        public int CraftIntBonus => Level("crafting");                  // 3단계 지능 +3
        public float TrapRadius => TrapRadii[Level("traps")];
        public static readonly float[] TrapRadii = { 0f, 6f, 10f, 15f };
        public float XpMult => 1f + 0.10f * Level("xp");                // 3단계 ×1.3
        public float MiningMult => 1f + 0.25f * Level("mining");        // 3단계 ×1.75
        public float ImpactMult => 1f - 0.20f * Level("fall");          // 3단계 ×0.4
        public float TempDriftMult => 1f - 0.20f * Level("temp");       // 3단계 ×0.4 (단열 ×2.5)
        /// <summary>온도 적응력은 체온이 37℃에서 멀어지는 쪽(위험 쪽)으로 끌려갈 때만 늦춘다. 37℃ 쪽으로 돌아오는 회복(운동 뒤 식기·불 쬐기)은 그대로(사용자 11-21).</summary>
        public static bool DriftAwayFromNormal(float body, float ambient) => ambient != body && (ambient - body) * (body - 37f) >= 0f;

        // ---- 포인트 ----
        /// <summary>회차 성과 → 포인트: 10분마다 1 + 새로 얻은 능력치 3마다 1 + 도달한 층 2마다 1 (상한 없음, 사용자 11-21). 3분 안에 끝나면 0.</summary>
        public static int Award(float runSeconds, int gainedLevels, int depthReached)
        {
            if (runSeconds < MinRunSeconds) return 0;
            int p = (int)(runSeconds / 600f) + Math.Max(0, gainedLevels) / 3 + Math.Max(0, depthReached) / 2;
            return p;
        }

        /// <summary>새 회차 시작: 이번 회차 지급 기록을 비운다.</summary>
        public void OnNewRun() => RunAward = 0;

        /// <summary>회차 끝: 성과 포인트를 준다. 같은 회차에서 다시 불리면 차이만 더한다(되살아나 더 버티면 늘 수 있음). 이번에 더한 값을 돌려준다.</summary>
        public int OnRunEnd(float runSeconds, int gainedLevels, int depthReached)
        {
            int award = Award(runSeconds, gainedLevels, depthReached);
            int add = Math.Max(0, award - RunAward);
            Points += add;
            RunAward = Math.Max(RunAward, award);
            return add;
        }

        // ---- 저장(key=value) ----
        public string Serialize()
        {
            var sb = new StringBuilder("version=1\n");
            sb.Append("points=").Append(Points.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("runAward=").Append(RunAward.ToString(CultureInfo.InvariantCulture)).Append('\n');
            for (int i = 0; i < Perks.Length; i++)
                sb.Append("perk.").Append(Perks[i].Id).Append('=').Append(Levels[i].ToString(CultureInfo.InvariantCulture)).Append('\n');
            return sb.ToString();
        }

        public static PerkState Parse(string text)
        {
            var s = new PerkState();
            if (string.IsNullOrEmpty(text)) return s;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line.Substring(0, eq).Trim();
                if (!int.TryParse(line.Substring(eq + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) || v < 0) continue;
                if (key == "points") s.Points = v;
                else if (key == "runAward") s.RunAward = v;
                else if (key.StartsWith("perk."))
                {
                    int i = IndexOf(key.Substring(5));
                    if (i >= 0) s.Levels[i] = Math.Min(v, Perks[i].MaxLevel);
                }
            }
            return s;
        }
    }
}
