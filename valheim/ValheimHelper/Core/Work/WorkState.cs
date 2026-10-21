using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ValheimHelper.Core.Npc;

namespace ValheimHelper.Core.Work
{
    public enum WorkKind { Gather, Explore, Make }

    /// <summary>대기열 칸을 꺼낼 수 있는 조건.</summary>
    public enum ReadyWhen { Always, Morning }

    /// <summary>
    /// 대기열 한 칸·하던 일을 데이터로 (risks.txt "작업 상태 저장 설계"). 플러그인이 이걸로 Start/Ready를 만든다.
    /// Resume이면 멈춘 일을 판단 없이 바로 다시 시작(큰 단계만: 채집은 물건·남은 수량, 세부 단계는 처음부터),
    /// 아니면 명령을 다시 판단한다(출발 전 판단·탐험 시작).
    /// </summary>
    public sealed class WorkItem
    {
        public WorkKind Kind;
        public bool Resume;
        /// <summary>대기열·"~부터 하고 갈게요"에 쓰는 짧은 이름.</summary>
        public string Label = "";
        /// <summary>채집: 물건 프리팹 이름.</summary>
        public string Item;
        /// <summary>채집: 수량(Resume이면 남은 수량).</summary>
        public int Amount;
        /// <summary>말한 사람 이름(캐릭터 ZDOID는 다시 접속하면 바뀌므로 이름만).</summary>
        public string Speaker;
        public string Target;
        public bool Urgent;
        public bool HasZone;
        /// <summary>고른 장소 구역(채집 목표). 기록용 — 이어 할 때 일이 다시 고른다.</summary>
        public ZoneXY Zone;
        public ReadyWhen Ready;
        /// <summary>
        /// Morning일 때: 이 게임 시각(초) 전에는 꺼내지 않는다 = 대답한 뒤 다음 아침 시작. "곧 밤이에요. 아침에 갈까요?"는 아직 낮일 때 묻는데,
        /// "밤이 아님"만 보면 바로 꺼내 같은 질문을 되풀이한다. 게임 시각은 월드와 같이 저장되므로 다시 불러와도 맞다.
        /// </summary>
        public double NotBefore;
        /// <summary>탐험 명령: 방향("east" 등)·게임 바이옴 이름. 없으면 null.</summary>
        public string Arg, Biome;
        /// <summary>탐험 이어 하기: 방향 벡터, 바이옴 번호, 알림 말("동쪽").</summary>
        public int DirX, DirY, BiomeId;
        public string Where;
        /// <summary>제작(Make): 세트 번호(0이면 Item 한 가지를 Amount개). 이어 할 때 계획을 현재 재고로 다시 세운다.</summary>
        public int SetNo;
    }

    /// <summary>질문하고 대답을 기다리는 상태(Departures 질문 대기).</summary>
    public sealed class PendingData
    {
        public Question Question;
        /// <summary>조건 질문의 필요한 것("healthMead").</summary>
        public string Condition;
        public bool HasPlace, HasAlternative;
        public ZoneXY Place, Alternative;
        /// <summary>대사 인자(item·n·foe…).</summary>
        public readonly Dictionary<string, string> Args = new Dictionary<string, string>();
        /// <summary>물은 시각(게임 시간 초, ZNet.GetTimeSeconds — 월드와 같이 저장됨).</summary>
        public double AskedAt;
        public string Speaker;
        // 명령(채집)
        public string Item;
        public int Amount;
        public bool Urgent;
        public string Target;
    }

    /// <summary>
    /// 저장 형식(다른 Book들처럼): 칸은 ";", 칸 안 필드는 ",", 필드는 "키=값". 값의 % ; , = | 는 %xx로 바꾼다.
    /// 모르는 키는 건너뛴다(뒤 버전 호환).
    /// </summary>
    public static class WorkState
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string SaveQueue(IEnumerable<WorkItem> items)
        {
            var parts = new List<string>();
            foreach (var w in items)
            {
                if (w == null) continue;
                var f = new List<string>
                {
                    "k=" + (int)w.Kind,
                    "l=" + Esc(w.Label),
                };
                if (w.Resume) f.Add("r=1");
                if (w.Item != null) f.Add("i=" + Esc(w.Item));
                if (w.Amount != 0) f.Add("n=" + w.Amount.ToString(Inv));
                if (w.Speaker != null) f.Add("s=" + Esc(w.Speaker));
                if (w.Target != null) f.Add("t=" + Esc(w.Target));
                if (w.Urgent) f.Add("u=1");
                if (w.HasZone) f.Add("z=" + w.Zone.X.ToString(Inv) + "|" + w.Zone.Y.ToString(Inv));
                if (w.Ready != ReadyWhen.Always) f.Add("w=" + (int)w.Ready);
                if (w.NotBefore != 0) f.Add("nb=" + w.NotBefore.ToString("R", Inv));
                if (w.Arg != null) f.Add("a=" + Esc(w.Arg));
                if (w.Biome != null) f.Add("b=" + Esc(w.Biome));
                if (w.DirX != 0 || w.DirY != 0) f.Add("d=" + w.DirX.ToString(Inv) + "|" + w.DirY.ToString(Inv));
                if (w.BiomeId != 0) f.Add("bi=" + w.BiomeId.ToString(Inv));
                if (w.Where != null) f.Add("wh=" + Esc(w.Where));
                if (w.SetNo != 0) f.Add("sn=" + w.SetNo.ToString(Inv));
                parts.Add(string.Join(",", f.ToArray()));
            }
            return string.Join(";", parts.ToArray());
        }

        public static List<WorkItem> LoadQueue(string text)
        {
            var list = new List<WorkItem>();
            if (string.IsNullOrEmpty(text)) return list;
            foreach (var part in text.Split(';'))
            {
                var f = Fields(part);
                if (!f.TryGetValue("k", out string k) || !TryInt(k, out int kind) || !Enum.IsDefined(typeof(WorkKind), kind)) continue;
                var w = new WorkItem { Kind = (WorkKind)kind };
                if (f.TryGetValue("l", out string v)) w.Label = v;
                w.Resume = f.TryGetValue("r", out v) && v == "1";
                if (f.TryGetValue("i", out v)) w.Item = v;
                if (f.TryGetValue("n", out v)) TryInt(v, out w.Amount);
                if (f.TryGetValue("s", out v)) w.Speaker = v;
                if (f.TryGetValue("t", out v)) w.Target = v;
                w.Urgent = f.TryGetValue("u", out v) && v == "1";
                if (f.TryGetValue("z", out v) && TryPair(v, out int zx, out int zy)) { w.HasZone = true; w.Zone = new ZoneXY(zx, zy); }
                if (f.TryGetValue("w", out v) && TryInt(v, out int r) && Enum.IsDefined(typeof(ReadyWhen), r)) w.Ready = (ReadyWhen)r;
                if (f.TryGetValue("nb", out v) && double.TryParse(v, NumberStyles.Float, Inv, out double nb)) w.NotBefore = nb;
                if (f.TryGetValue("a", out v)) w.Arg = v;
                if (f.TryGetValue("b", out v)) w.Biome = v;
                if (f.TryGetValue("d", out v) && TryPair(v, out int dx, out int dy)) { w.DirX = dx; w.DirY = dy; }
                if (f.TryGetValue("bi", out v)) TryInt(v, out w.BiomeId);
                if (f.TryGetValue("wh", out v)) w.Where = v;
                if (f.TryGetValue("sn", out v)) TryInt(v, out w.SetNo);
                if (w.Kind == WorkKind.Gather && (string.IsNullOrEmpty(w.Item) || w.Amount <= 0)) continue; // 깨진 칸
                if (w.Kind == WorkKind.Make && (w.Amount <= 0 || (w.SetNo <= 0 && string.IsNullOrEmpty(w.Item)))) continue; // 깨진 칸
                list.Add(w);
            }
            return list;
        }

        /// <summary>질문 대기 저장. null이면 빈 문자열(기다리는 질문 없음).</summary>
        public static string SavePending(PendingData p)
        {
            if (p == null) return "";
            var f = new List<string>
            {
                "q=" + (int)p.Question,
                "at=" + p.AskedAt.ToString("R", Inv),
            };
            if (p.Condition != null) f.Add("c=" + Esc(p.Condition));
            if (p.HasPlace) f.Add("p=" + p.Place.X.ToString(Inv) + "|" + p.Place.Y.ToString(Inv));
            if (p.HasAlternative) f.Add("alt=" + p.Alternative.X.ToString(Inv) + "|" + p.Alternative.Y.ToString(Inv));
            if (p.Speaker != null) f.Add("s=" + Esc(p.Speaker));
            if (p.Item != null) f.Add("i=" + Esc(p.Item));
            if (p.Amount != 0) f.Add("n=" + p.Amount.ToString(Inv));
            if (p.Urgent) f.Add("u=1");
            if (p.Target != null) f.Add("t=" + Esc(p.Target));
            // 대사 인자는 같은 칸 안에서 "x." 접두사로(키 순서 고정: 저장 문자열이 같으면 ZDO에 다시 쓰지 않으려고)
            var keys = new List<string>(p.Args.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var key in keys) f.Add("x." + Esc(key) + "=" + Esc(p.Args[key]));
            return string.Join(",", f.ToArray());
        }

        public static PendingData LoadPending(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var f = Fields(text);
            if (!f.TryGetValue("q", out string q) || !TryInt(q, out int question) || !Enum.IsDefined(typeof(Question), question)) return null;
            if (!f.TryGetValue("at", out string at) || !double.TryParse(at, NumberStyles.Float, Inv, out double askedAt)) return null;
            var p = new PendingData { Question = (Question)question, AskedAt = askedAt };
            if (f.TryGetValue("c", out string v)) p.Condition = v;
            if (f.TryGetValue("p", out v) && TryPair(v, out int px, out int py)) { p.HasPlace = true; p.Place = new ZoneXY(px, py); }
            if (f.TryGetValue("alt", out v) && TryPair(v, out int ax, out int ay)) { p.HasAlternative = true; p.Alternative = new ZoneXY(ax, ay); }
            if (f.TryGetValue("s", out v)) p.Speaker = v;
            if (f.TryGetValue("i", out v)) p.Item = v;
            if (f.TryGetValue("n", out v)) TryInt(v, out p.Amount);
            p.Urgent = f.TryGetValue("u", out v) && v == "1";
            if (f.TryGetValue("t", out v)) p.Target = v;
            foreach (var kv in f)
                if (kv.Key.StartsWith("x.", StringComparison.Ordinal)) p.Args[kv.Key.Substring(2)] = kv.Value;
            if (string.IsNullOrEmpty(p.Item) || p.Amount <= 0) return null;
            return p;
        }

        /// <summary>
        /// 다음 아침 시작(게임 초): 바닐라 EnvMan.SkipToMorning과 같은 셈 — 하루 길이의 0.15가 아침 시작, 자정 넘어 새벽이면 그날 아침.
        /// </summary>
        public static double NextMorning(double now, double dayLength)
        {
            if (dayLength <= 0) return now;
            int day = (int)((now - dayLength * 0.15) / dayLength);
            return (day + 1) * dayLength + dayLength * 0.15;
        }

        /// <summary>대기열 칸을 지금 꺼낼 수 있나(night = 지금 밤인지).</summary>
        public static bool IsReady(WorkItem w, double now, bool night) =>
            w.Ready != ReadyWhen.Morning || (now >= w.NotBefore && !night);

        /// <summary>불러온 질문이 이미 3분(Replies.Timeout)을 넘었는지 — 넘었으면 보류 처리.</summary>
        public static bool Expired(PendingData p, double now) => p != null && now - p.AskedAt > Replies.Timeout;

        // ---- 공통 ----

        static Dictionary<string, string> Fields(string part)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(part)) return d;
            foreach (var kv in part.Split(','))
            {
                int eq = kv.IndexOf('=');
                if (eq <= 0) continue;
                d[Unesc(kv.Substring(0, eq))] = Unesc(kv.Substring(eq + 1));
            }
            return d;
        }

        static bool TryInt(string s, out int n) => int.TryParse(s, NumberStyles.Integer, Inv, out n);

        static bool TryPair(string s, out int a, out int b)
        {
            a = b = 0;
            int bar = s.IndexOf('|');
            return bar > 0 && TryInt(s.Substring(0, bar), out a) && TryInt(s.Substring(bar + 1), out b);
        }

        public static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c == '%' || c == ';' || c == ',' || c == '=' || c == '|') sb.Append('%').Append(((int)c).ToString("X2", Inv));
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public static string Unesc(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('%') < 0) return s ?? "";
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '%' && i + 2 < s.Length &&
                    int.TryParse(s.Substring(i + 1, 2), NumberStyles.HexNumber, Inv, out int code))
                {
                    sb.Append((char)code);
                    i += 2;
                }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }
    }
}
