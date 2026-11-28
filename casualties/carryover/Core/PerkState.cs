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
    /// 전부 사는 데 199점(아공간 20단계 115점).
    /// </summary>
    public sealed class PerkState
    {
        public static readonly PerkDef[] Perks =
        {
            new PerkDef("subspace", "아공간", "무게 없는 가방 용량 +20u (같은 물건은 한 묶음, 몸 무게에 안 더해짐, 다음 회차로 이어짐)", 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 6, 6, 7, 7, 7, 8, 8, 8, 9, 9),
            new PerkDef("lockpick", "자물쇠 달인", "자물쇠 따기 허용 각도 +25%", 1, 2, 3, 4, 5),
            new PerkDef("traps", "함정 감지", "가까운 함정 표시 (반경 6 → 10 → 15 → 20 → 25)", 2, 3, 4, 5, 6),
            new PerkDef("mining", "광부", "벽·바위를 칠 때 피해 +25% (맨손·도구)", 1, 2, 3, 4, 5, 6),
            new PerkDef("vision", "시야 적응력", "주변광 +단계 (4단계 = 낙원 「밝음」), 반경 안 벽 너머도 보임 (4단계는 시야 가림 없음)", 2, 3, 4, 5),
            new PerkDef("temp", "온도 적응력", "체온이 정상에서 멀어지는 속도 -20% (회복은 그대로)", 2, 3, 4, 5),
        };

        /// <summary>뺀 퍽(사용자 11-24: 숙련 제작·경험치 증가는 의미 없음, 11-28: 튼튼한 다리). 옛 파일에 단계가 있으면 쓴 포인트를 돌려준다(한 번만 — 다시 저장하면 키가 사라짐).</summary>
        public static readonly System.Collections.Generic.Dictionary<string, int[]> RemovedCosts = new System.Collections.Generic.Dictionary<string, int[]>
        {
            { "crafting", new[] { 1, 2, 3 } },
            { "xp", new[] { 2, 3, 4 } },
            { "fall", new[] { 2, 3, 4, 5 } },   // 튼튼한 다리 → 시야 적응력(사용자 11-28)
        };

        /// <summary>Parse 때 뺀 퍽에서 돌려준 포인트(0보다 크면 바로 저장해야 다시 안 돌려줌).</summary>
        public int Refunded;

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
        public int SubspaceSlots => Level("subspace");                  // 퍽 단계 1~20
        public float SubspaceMaxWeight => 20f * Level("subspace");     // 1단계 20u … 20단계 400u (사용자 11-22: 칸 수 대신 무게)
        public float LockpickMult => 1f + 0.25f * Level("lockpick");    // 단계마다 +25%

        public float TrapRadius => TrapRadii[Level("traps")];
        public static readonly float[] TrapRadii = { 0f, 6f, 10f, 15f, 20f, 25f };
        public float MiningMult => 1f + 0.25f * Level("mining");        // 단계마다 +25%
        public float VisionRadius => VisionRadii[Level("vision")];
        public static readonly float[] VisionRadii = { 0f, 3f, 5f, 7f, 9f };   // 블록 1칸 = 1
        public float TempDriftMult => 1f - 0.20f * Level("temp");       // 4단계 ×0.2
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
                    else if (RemovedCosts.TryGetValue(key.Substring(5), out var rc)) { int r = 0; for (int k = 0; k < Math.Min(v, rc.Length); k++) r += rc[k]; s.Refunded += r; }
                }
            }
            s.Points += s.Refunded;
            return s;
        }
    }
}
