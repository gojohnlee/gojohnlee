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
        public float DeathWeight;
        public bool Boss;
        public string TopFoe;
        /// <summary>그 곳 바이옴에서 막아야 할 피해(Resist.ForBiome, "Poison" 등). 장비로 이미 막거나 없으면 null.</summary>
        public string Resist;
        /// <summary>그 바이옴에서 겪은 적 하나를 지금 장비(상자의 더 좋은 것 포함)로 상대할 때 비율(PlaceBook.BiomeRisk). HardFrom 넘으면 "이 장비론 무리".</summary>
        public float GearRatio;
        /// <summary>GearRatio를 정한 적 종류.</summary>
        public string GearFoe;
        /// <summary>바이옴 이름(대사용, "평원").</summary>
        public string BiomeName;
        public bool GearShort => GearRatio >= Departure.HardFrom;
        /// <summary>거점 범위(125m) 안: 위험해도 피해 바꿔 가지 않는다(습격 한 번에 집 앞 자원을 버리지 않게 — 도망 뒤 목표 FleeBook과 같은 원칙).</summary>
        public bool InBase;
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
        /// <summary>고철(철)인데 아는 곳도 없고 갈 지하묘지(열린 곳, 열쇠가 있으면 닫힌 곳)도 없다.</summary>
        public bool NoCrypt;
        /// <summary>NoCrypt의 까닭이 다 캔 지하묘지다(새 곳이 필요). 아니면 문이 닫혀서.</summary>
        public bool CryptsMinedOut;
        /// <summary>묻혀 숨는 광맥(산 은)에서 나는 물건인데 아는 곳(드러난 광맥)이 없고 위시본도 아직 없다(⑪ 3).</summary>
        public bool NeedFinder;
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
        /// <summary>호위병 이름(사용자 10-26). 호위병이 없으면 null.</summary>
        public string Guard;
        /// <summary>호위병이 다른 일꾼을 따라가는 중·묘비 회수 등으로 지금 못 옴(쉬게 되면 같이 출발).</summary>
        public bool GuardBusy;
    }

    public enum DepartAction
    {
        Go,
        /// <summary>남은 곳이 다 위험해서 호위병을 데리고 간다(묻지 않음 — 사용자 결정 2026-10-26).</summary>
        GoTogether,
        /// <summary>남은 곳이 다 위험한데 호위병이 바쁘다 → 거점에서 기다렸다가 호위병이 쉬게 되면 같이 출발(대기열, 사용자 10-26).</summary>
        WaitGuard,
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
        /// <summary>위험해서 조용히 바꿔 고른 덜 위험한 곳(이때 Place도 이것). 없으면 null.</summary>
        public PlaceOption Alternative;
        /// <summary>이번 일에서 피할 위험한 곳(덜 위험한 곳이 하나라도 있을 때만 채움).</summary>
        public readonly List<PlaceOption> Avoid = new List<PlaceOption>();
        /// <summary>조건 질문의 필요한 것: "healthMead", "resist:Poison" 같은 저항 미드(Resist.Condition), 또는 "gear"(더 좋은 장비, ⑫ 다).</summary>
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
    /// 순서: 막힘(거절) → 하던 일(대기열, 급하면 끼어듦) → 질문 하나(밤 > 재고) → 위험하면 조용히 덜 위험한 곳, 없으면 호위병과 같이(바쁘면 기다림) → 출발 + 덧붙이는 말 하나.
    /// </summary>
    public static class Departure
    {
        public const float NoneBelow = 0.15f, HardFrom = 1.0f;
        public const float DeathRagnar = 0.25f, DeathFloki = 0.5f;
        public const float WalkSpeed = 5f, FarDistance = 600f, NightNearHome = 100f;
        public const int LowSkill = 20;

        public static float FightLine(Personality p) => p == Personality.Ragnar ? Danger.FightRagnar : Danger.FightFloki;

        public static DangerLevel Level(Personality p, PlaceOption o)
        {
            if (o.Boss || o.Ratio > Danger.AvoidRatio) return DangerLevel.Impossible;
            if (o.Ratio >= HardFrom) return DangerLevel.Hard;
            if (o.Ratio >= FightLine(p)) return DangerLevel.Tight;
            if (o.Ratio >= NoneBelow) return DangerLevel.Caution;
            return DangerLevel.None;
        }

        static bool DiedThere(Personality p, PlaceOption o) => o.DeathWeight > (p == Personality.Ragnar ? DeathRagnar : DeathFloki);

        /// <summary>위험한 곳: 빠듯 이상, 이 장비론 그 바이옴 무리, 또는 죽은 곳.</summary>
        public static bool Risky(Personality p, PlaceOption o, DangerLevel level) => level >= DangerLevel.Tight || o.GearShort || DiedThere(p, o);

        /// <summary>대안: 위험하지 않은 곳 중 가장 가까운 곳(거리 제한 없음 — 사용자 결정 2026-10-26).</summary>
        public static PlaceOption Alternative(Personality p, List<PlaceOption> places, PlaceOption from)
        {
            PlaceOption best = null;
            foreach (var o in places)
            {
                if (o == from || Risky(p, o, Level(p, o))) continue;
                if (best == null || o.Distance < best.Distance) best = o;
            }
            return best;
        }

        /// <summary>다 위험할 때: from보다 덜 위험한(죽은 기록 → 위험 단계 순) 곳 중 가장 덜 위험하고, 같으면 가까운 곳. 없으면 null.</summary>
        public static PlaceOption Safer(Personality p, List<PlaceOption> places, PlaceOption from)
        {
            PlaceOption best = null;
            foreach (var o in places)
            {
                if (o == from || o.InBase || !Worse(p, from, o)) continue;
                if (best == null || Worse(p, best, o) || !Worse(p, o, best) && o.Distance < best.Distance) best = o;
            }
            return best;
        }

        /// <summary>a가 b보다 위험한가: 죽은 기록이 먼저, 그다음 위험 단계.</summary>
        static bool Worse(Personality p, PlaceOption a, PlaceOption b)
        {
            bool da = DiedThere(p, a), db = DiedThere(p, b);
            return da != db ? da : Level(p, a) > Level(p, b);
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
            if (i.NoCrypt) return i.CryptsMinedOut ? Refuse(r, "depart.cryptsEmpty", "지하묘지 다 캠") : Refuse(r, "depart.noCrypt", "열린 지하묘지 없음");
            if (i.Tool == ToolState.None) return Refuse(r, "noTool", "연장 없음");
            if (i.Tool == ToolState.Weak) return Refuse(r, "toolTooWeak", "연장 약함");
            if (i.NeedFinder && i.Places.Count == 0) return Refuse(r, "depart.needFinder", "묻힌 광맥뿐, 위시본 없음");

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

            // 3) 질문 하나(밤·재고만). 위험은 묻거나 거절하지 않는다(사용자 결정 2026-10-25):
            //    위험한 곳이면 조용히 덜 위험한 곳(거리 무관)으로, 그런 곳이 없으면 호위병을 데려간다(바쁘면 기다렸다 같이 — 사용자 결정 2026-10-26).
            if (t != null)
            {
                var level = Level(p, t);
                r.Args["foe"] = t.TopFoe ?? "";
                r.Reason += $"위험 {t.Ratio:0.00} {level}, 죽음 {t.DeathWeight:0.00}";
                if (level == DangerLevel.Tight && i.HealthMeads > 0 && t.RatioWithMead < FightLine(p))
                    level = DangerLevel.Caution; // 가진 미드로 버틴다

                if (nightSoon) return Ask(r, Question.Night, null, stockEnough, false);
                if (stockEnough) return Ask(r, Question.Stock, null, false, false);

                PlaceOption alt = Risky(p, t, level) && !t.InBase ? Alternative(p, i.Places, t) : null;
                if (alt != null)
                {
                    r.Alternative = r.Place = t = alt;
                    level = Level(p, alt);
                    r.Seconds = Estimate(i, alt);
                    r.Args["minutes"] = Minutes5(r.Seconds).ToString();
                    r.Args["foe"] = alt.TopFoe ?? "";
                    r.Reason += $" → 덜 위험한 곳 {alt.Distance:0}m(위험 {alt.Ratio:0.00})";
                }
                else if (Risky(p, t, level) && !t.InBase && Safer(p, i.Places, t) is PlaceOption less)
                {
                    // 다 위험해도 호위병과 갈 곳은 그중 덜 위험한 곳(⑬ 라-2: 시커 셋 본 곳보다 아무것도 못 본 안개땅), 더 위험한 곳은 이번 일에서 피함
                    foreach (var o in i.Places)
                        if (o != less && !o.InBase && Worse(p, o, less)) r.Avoid.Add(o);
                    r.Place = t = less;
                    level = Level(p, less);
                    r.Seconds = Estimate(i, less);
                    r.Args["minutes"] = Minutes5(r.Seconds).ToString();
                    r.Args["foe"] = less.TopFoe ?? "";
                    r.Reason += $" → 그나마 덜 위험한 곳 {less.Distance:0}m(위험 {less.Ratio:0.00})";
                }
                if (!Risky(p, t, level)) // 덜 위험한 곳으로 가니 위험한 곳은 이번 일에서 피한다(일이 스스로 고르는 구역에서도)
                {
                    foreach (var o in i.Places)
                        if (o != t && !o.InBase && Risky(p, o, Level(p, o))) r.Avoid.Add(o);
                }
                else if (i.Guard != null)
                {
                    r.Args["partner"] = i.Guard;
                    if (i.GuardBusy) return Go(r, DepartAction.WaitGuard, "depart.waitGuard", "남은 곳이 다 위험·호위병 바쁨 → 기다렸다 같이");
                    return Go(r, DepartAction.GoTogether, "depart.together", "남은 곳이 다 위험 → 호위병과 같이");
                }
                else if (t.GearShort && level != DangerLevel.Impossible) // 그 바이옴 적 하나도 지금 장비론 못 당함(⑫ 다)
                {
                    r.Args["biome"] = t.BiomeName ?? "";
                    if (t.GearFoe != null) r.Args["foe"] = t.GearFoe;
                    r.Reason += $", 장비 {t.GearRatio:0.00}";
                    return Go(r, DepartAction.Go, "depart.gear", "장비 모자라도 감");
                }
                else return Go(r, DepartAction.Go, "depart.danger", "위험해도 감");

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
