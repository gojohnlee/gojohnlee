using System;
using System.Collections.Generic;
using ValheimHelper.Core.Work;

namespace ValheimHelper.Core.Npc
{
    public enum ToolState { Ok, Weak, None }

    /// <summary>후보 장소 하나(그 물건이 있다고 아는 구역). 비율은 플러그인이 PlaceBook.Risk로 계산해 넣는다.</summary>
    public sealed class PlaceOption
    {
        public ZoneXY Zone;
        /// <summary>거점에서 수평 거리(m).</summary>
        public float Distance;
        /// <summary>혼자, 최대 체력으로 잃을 체력 비율(감쇠 반영).</summary>
        public float Ratio;
        /// <summary>체력 미드 하나를 더 마신다고 쳤을 때 비율.</summary>
        public float RatioWithMead;
        /// <summary>한가한 동료와 같이 갈 때 비율. 동료가 없거나 바쁘면 NaN.</summary>
        public float RatioTogether = float.NaN;
        public float DeathWeight;
        public bool Boss;
        public string TopFoe;
        /// <summary>그 곳 바이옴에서 막아야 할 피해(Resist.ForBiome, "Poison" 등). 장비로 이미 막거나 없으면 null.</summary>
        public string Resist;
    }

    public sealed class DepartureInput
    {
        public Personality Personality;
        public string Item;
        public int Amount;
        /// <summary>"빨리·이거부터" — 하던 일을 멈추고 이것부터(설계 B-5, 사용자 확정).</summary>
        public bool Urgent;
        /// <summary>하던 일 이름(대사용). 없으면 null.</summary>
        public string CurrentJob;
        /// <summary>그 물건이 있다고 아는 곳, 거점에서 가까운 순. 비어 있으면 모르는 물건(찾기·새 땅).</summary>
        public List<PlaceOption> Places = new List<PlaceOption>();
        public ToolState Tool;
        /// <summary>범위 밖 재료(배·보스 전리품 등).</summary>
        public bool OutOfScope;
        /// <summary>고철(철)인데 아는 곳도 없고 문이 열린 지하묘지도 없다.</summary>
        public bool NoCrypt;
        public bool Fed;
        public bool FoodInChest = true;
        public int HealthMeads;
        /// <summary>가진(짐·상자) 저항 미드 종류("Poison" 등).</summary>
        public HashSet<string> ResistMeads = new HashSet<string>();
        /// <summary>그 일의 스킬. 모르면 -1.</summary>
        public int Skill = -1;
        public int Stock;
        /// <summary>한 번에 들 수 있는 수. 0이면 셈하지 않음.</summary>
        public int CarryPerTrip;
        /// <summary>밤까지 남은 초(이미 밤이면 0). 밤을 신경 안 쓰면 +∞.</summary>
        public float SecondsToNight = float.PositiveInfinity;
        /// <summary>한 개 모으는 데 드는 초.</summary>
        public float SecondsPerUnit = 4f;
        public bool KnowsPortal;
    }

    public enum DepartAction
    {
        Go,
        /// <summary>한가한 동료와 같이 간다(3-2장: 묻지 않음).</summary>
        GoTogether,
        Ask,
        /// <summary>"~부터 하고 갈게요" → 대기열 끝. 꺼낼 때 다시 판단한다.</summary>
        Queue,
        Refuse,
    }

    public enum Question { None, Alternative, Escort, DiedThere, Condition, Night, Stock }

    public enum DangerLevel { None, Caution, Tight, Hard, Impossible }

    public sealed class DepartureResult
    {
        public DepartAction Action;
        /// <summary>하던 일을 멈추고 이걸 먼저(멈춘 일은 대기열 맨 앞).</summary>
        public bool Interrupts;
        public Question Question;
        /// <summary>갈 곳(원래 목표). 모르는 물건이면 null.</summary>
        public PlaceOption Place;
        /// <summary>제안한(또는 고른) 대안. 없으면 null.</summary>
        public PlaceOption Alternative;
        /// <summary>조건 질문의 필요한 것: "healthMead" 또는 "resist:Poison" 같은 저항 미드(Resist.Condition).</summary>
        public string Condition;
        /// <summary>대사 키. null이면 플러그인의 평소 복창(gatherStart/gatherKnown/searching).</summary>
        public string Key;
        /// <summary>질문에 한 마디로 붙일 대사 키({extra}). 없으면 null.</summary>
        public string ExtraKey;
        public readonly Dictionary<string, string> Args = new Dictionary<string, string>();
        public float Seconds;
        /// <summary>로그·시험용 판단 근거.</summary>
        public string Reason = "";
    }

    /// <summary>
    /// 출발 전 판단 (구상서 8-1장, 설계 docs/valheim/design-depart-craft.txt B장). 규칙만, 대사는 키로 돌려준다.
    /// 순서: 막힘(거절) → 하던 일(대기열, 급하면 끼어듦) → 질문 하나(위험·대안 > 죽은 곳 > 조건 > 밤 > 재고) → 출발 + 덧붙이는 말 하나.
    /// </summary>
    public static class Departure
    {
        public const float NoneBelow = 0.15f, HardFrom = 1.0f;
        public const float DeathScv = 0.25f, DeathAlarak = 0.5f;
        public const float WalkSpeed = 5f, FarDistance = 600f, NightNearHome = 100f;
        public const int LowSkill = 20;

        public static float FightLine(Personality p) => p == Personality.Alarak ? Danger.FightAlarak : Danger.FightScv;

        public static DangerLevel Level(Personality p, PlaceOption o)
        {
            if (o.Boss || o.Ratio > Danger.AvoidRatio) return DangerLevel.Impossible;
            if (o.Ratio >= HardFrom) return DangerLevel.Hard;
            if (o.Ratio >= FightLine(p)) return DangerLevel.Tight;
            if (o.Ratio >= NoneBelow) return DangerLevel.Caution;
            return DangerLevel.None;
        }

        static bool DiedThere(Personality p, PlaceOption o) => o.DeathWeight > (p == Personality.Alarak ? DeathAlarak : DeathScv);

        static bool Together(Personality p, PlaceOption o) => !float.IsNaN(o.RatioTogether) && o.RatioTogether < FightLine(p) && !o.Boss;

        /// <summary>대안: 위험 "주의" 이하, 죽은 곳 아님, 원래 곳의 2배 또는 +300m 안에서 가장 가까운 곳.</summary>
        public static PlaceOption Alternative(Personality p, List<PlaceOption> places, PlaceOption from)
        {
            float limit = Math.Max(from.Distance * 2f, from.Distance + 300f);
            PlaceOption best = null;
            foreach (var o in places)
            {
                if (o == from || o.Distance > limit || Level(p, o) > DangerLevel.Caution || DiedThere(p, o)) continue;
                if (best == null || o.Distance < best.Distance) best = o;
            }
            return best;
        }

        public static int Trips(int amount, int carry) => carry <= 0 ? 1 : Math.Max(1, (amount + carry - 1) / carry);

        /// <summary>예상 소요(초): 왕복 이동 × 왕복 횟수 + 개당 초 × 수량(스킬 10 → ×1.0, 70 → ×0.7).</summary>
        public static float Estimate(DepartureInput i, PlaceOption at)
        {
            float travel = at != null ? at.Distance * 2f / WalkSpeed * Trips(i.Amount, i.CarryPerTrip) : 0f;
            float skill = i.Skill < 0 ? 1f : 1f - 0.3f * Math.Min(1f, Math.Max(0f, (i.Skill - 10) / 60f));
            return travel + i.SecondsPerUnit * i.Amount * skill;
        }

        /// <summary>대사용 분: 5분 단위 올림(최소 5).</summary>
        public static int Minutes5(float seconds) => Math.Max(5, (int)Math.Ceiling(seconds / 300f) * 5);

        public static DepartureResult Judge(DepartureInput i)
        {
            var r = new DepartureResult();
            var p = i.Personality;
            r.Args["item"] = i.Item ?? "";
            r.Args["n"] = i.Amount.ToString();

            // 1) 막힘
            if (i.OutOfScope) return Refuse(r, "depart.outOfScope", "범위 밖");
            if (i.NoCrypt) return Refuse(r, "depart.noCrypt", "열린 지하묘지 없음");
            if (i.Tool == ToolState.None) return Refuse(r, "noTool", "연장 없음");
            if (i.Tool == ToolState.Weak) return Refuse(r, "toolTooWeak", "연장 약함");

            // 2) 하던 일
            if (i.CurrentJob != null)
            {
                if (!i.Urgent)
                {
                    r.Action = DepartAction.Queue;
                    r.Key = "depart.busy";
                    r.Args["job"] = i.CurrentJob;
                    r.Reason = "하던 일 → 대기열";
                    return r;
                }
                r.Interrupts = true;
                r.Reason = "급함 → 끼어듦; ";
            }

            PlaceOption t = i.Places.Count > 0 ? i.Places[0] : null;
            r.Place = t;
            r.Seconds = Estimate(i, t);
            r.Args["minutes"] = Minutes5(r.Seconds).ToString();
            bool stockEnough = i.Amount > 0 && i.Stock >= i.Amount;
            bool nightSoon = t != null && t.Distance > NightNearHome && i.SecondsToNight < r.Seconds;
            r.Args["stock"] = i.Stock.ToString();

            // 3) 질문 하나
            if (t != null)
            {
                var level = Level(p, t);
                r.Args["foe"] = t.TopFoe ?? "";
                r.Reason += $"위험 {t.Ratio:0.00} {level}, 죽음 {t.DeathWeight:0.00}";
                PlaceOption alt = level >= DangerLevel.Tight || DiedThere(p, t) ? Alternative(p, i.Places, t) : null;

                if (level >= DangerLevel.Hard)
                {
                    if (level == DangerLevel.Hard && Together(p, t))
                        return Go(r, DepartAction.GoTogether, "depart.together", "동료와 합치면 됨");
                    return alt != null ? Ask(r, Question.Alternative, alt, stockEnough, nightSoon) : Ask(r, Question.Escort, null, stockEnough, nightSoon);
                }
                if (DiedThere(p, t))
                    return Ask(r, Question.DiedThere, alt, stockEnough, nightSoon);
                if (level == DangerLevel.Tight)
                {
                    if (i.HealthMeads > 0 && t.RatioWithMead < FightLine(p))
                        level = DangerLevel.Caution; // 가진 미드로 버틴다
                    else if (i.HealthMeads == 0 && t.RatioWithMead < FightLine(p))
                    {
                        r.Condition = "healthMead";
                        return Ask(r, Question.Condition, alt, stockEnough, nightSoon);
                    }
                    else if (Together(p, t))
                        return Go(r, DepartAction.GoTogether, "depart.together", "동료와 합치면 됨");
                    else
                        return alt != null ? Ask(r, Question.Alternative, alt, stockEnough, nightSoon) : Ask(r, Question.Escort, null, stockEnough, nightSoon);
                }

                if (nightSoon) return Ask(r, Question.Night, null, stockEnough, false);
                if (stockEnough) return Ask(r, Question.Stock, null, false, false);

                // 4) 출발 + 덧붙이는 말 하나
                // 그 바이옴 저항 미드가 없으면 묻지 않고 투덜대고 간다(사용자 결정 2026-10-22, 예전엔 "독 저항 미드 하나만 주시면 갈게요"로 기다림)
                if (level == DangerLevel.Caution && t.Resist != null && !i.ResistMeads.Contains(t.Resist))
                {
                    r.Args["mead"] = Resist.MeadName(t.Resist);
                    return Go(r, DepartAction.Go, "depart.noResist", "저항 미드 없음");
                }
                if (level == DangerLevel.Caution) return Go(r, DepartAction.Go, "depart.warn", "주의");
            }
            else if (stockEnough) return Ask(r, Question.Stock, null, false, false);

            if (!i.Fed && !i.FoodInChest) return Go(r, DepartAction.Go, "depart.hungry", "배고픔");
            if (t != null && t.Distance > FarDistance && !i.KnowsPortal)
            {
                r.Args["dist"] = ((int)t.Distance).ToString();
                return Go(r, DepartAction.Go, "depart.far", "멂");
            }
            int trips = Trips(i.Amount, i.CarryPerTrip);
            if (trips >= 2)
            {
                r.Args["trips"] = trips.ToString();
                return Go(r, DepartAction.Go, "depart.trips", $"왕복 {trips}");
            }
            if (i.Skill >= 0 && i.Skill < LowSkill) return Go(r, DepartAction.Go, "depart.lowSkill", "스킬 낮음");
            return Go(r, DepartAction.Go, null, "여유");
        }

        static DepartureResult Refuse(DepartureResult r, string key, string why)
        {
            r.Action = DepartAction.Refuse;
            r.Key = key;
            r.Reason += why;
            return r;
        }

        static DepartureResult Go(DepartureResult r, DepartAction a, string key, string why)
        {
            r.Action = a;
            r.Key = key;
            r.Reason += (r.Reason.Length > 0 ? " → " : "") + why;
            return r;
        }

        static DepartureResult Ask(DepartureResult r, Question q, PlaceOption alt, bool stockNote, bool nightNote)
        {
            r.Action = DepartAction.Ask;
            r.Question = q;
            r.Alternative = alt;
            r.Key = "ask." + Lower(q.ToString());
            if (stockNote) r.ExtraKey = "note.stock";
            else if (nightNote) r.ExtraKey = "note.night";
            r.Reason += (r.Reason.Length > 0 ? " → " : "") + "질문 " + q + (alt != null ? " (대안 있음)" : "");
            return r;
        }

        static string Lower(string s) => char.ToLowerInvariant(s[0]) + s.Substring(1);
    }

    public enum Reply { Yes, No, GoAnyway, Escort, Elsewhere, Timeout }

    public enum ReplyAction
    {
        /// <summary>대안 장소로 간다.</summary>
        GoAlternative,
        /// <summary>원래 곳으로 그냥 간다.</summary>
        GoAlone,
        /// <summary>원래 곳으로 혼자, 투덜대며("묘비 찾으러 와 주셔야 해요").</summary>
        GoGrumble,
        /// <summary>대답한 사람과 같이 간다(동행).</summary>
        GoEscort,
        /// <summary>조건을 다시 확인하고 되면 간다.</summary>
        Recheck,
        /// <summary>아침까지 대기열에 둔다.</summary>
        WaitMorning,
        /// <summary>보류: 접어 두고 다음 일.</summary>
        Hold,
        Cancel,
        /// <summary>이 질문에 맞지 않는 대답(무시하고 계속 기다림).</summary>
        NotApplicable,
    }

    /// <summary>질문 뒤 대답 (설계 B-7 표). 3분 안에 대답이 없으면 보류.</summary>
    public static class Replies
    {
        public const double Timeout = 180.0;

        public static ReplyAction Resolve(Question q, bool hasAlternative, Reply a)
        {
            if (a == Reply.Timeout) return ReplyAction.Hold;
            switch (q)
            {
                case Question.Alternative:
                    switch (a)
                    {
                        case Reply.Yes: case Reply.Elsewhere: return ReplyAction.GoAlternative;
                        case Reply.GoAnyway: return ReplyAction.GoGrumble;
                        case Reply.Escort: return ReplyAction.GoEscort;
                        default: return ReplyAction.Hold;
                    }
                case Question.Escort:
                case Question.DiedThere:
                case Question.Condition:
                    switch (a)
                    {
                        case Reply.Yes: return q == Question.Condition ? ReplyAction.Recheck : ReplyAction.GoEscort;
                        case Reply.GoAnyway: return ReplyAction.GoGrumble;
                        case Reply.Escort: return ReplyAction.GoEscort;
                        case Reply.Elsewhere: return hasAlternative ? ReplyAction.GoAlternative : ReplyAction.NotApplicable;
                        default: return ReplyAction.Hold;
                    }
                case Question.Night:
                    switch (a)
                    {
                        case Reply.Yes: return ReplyAction.WaitMorning;
                        case Reply.GoAnyway: return ReplyAction.GoAlone;
                        case Reply.Escort: return ReplyAction.GoEscort;
                        case Reply.Elsewhere: return ReplyAction.NotApplicable;
                        default: return ReplyAction.Cancel;
                    }
                case Question.Stock:
                    switch (a)
                    {
                        case Reply.Yes: case Reply.GoAnyway: return ReplyAction.GoAlone;
                        case Reply.Escort: return ReplyAction.GoEscort;
                        case Reply.Elsewhere: return ReplyAction.NotApplicable;
                        default: return ReplyAction.Cancel;
                    }
            }
            return ReplyAction.NotApplicable;
        }
    }
}
