using System;
using System.Collections.Generic;
using ValheimHelper.Core.Work;

namespace ValheimHelper.Core.Npc
{
    /// <summary>잡담 거리가 되는 일 (사용자 결정 2026-10-23 "잡담").</summary>
    public enum BanterEvent { Died, Failed, Haul, Rescued, SkillUp, Fled, Raid, Biome, Back, Boss, Weather }

    /// <summary>
    /// 잡담에 쓸 기억. 플러그인이 장소 기억·지식·스킬·밭에서 그때그때 채운다. 없는 칸(null)의 주제는 고르지 않는다.
    /// 이름은 보이는 이름(적·물건·바이옴 한국어), 방향은 거점에서 본 8방위("북동쪽").
    /// </summary>
    public sealed class BanterFacts
    {
        public string DeathFoe, DeathDir;
        public int Deaths;
        public string FledFoe, BeatenFoe;
        public string Biome, BiomeDir;
        public string Item, ItemDir;
        public string Crop;
        public string PitDir;
        public bool Night;
        /// <summary>곁에 있는 플레이어 이름(없으면 null) — 있으면 가끔 플레이어에게 말을 건다.</summary>
        public string Player;
        /// <summary>NPC 자리(0 = 1호, 1 = 2호)마다 가장 높은 솜씨와 그 수준.</summary>
        public readonly string[] Skill = new string[2];
        public readonly int[] Level = new int[2];

        /// <summary>장소 기억에서 가장 많이 죽은 곳·도망친 적·이긴 적·구덩이를 뽑는다. foeName = 적 종류 → 보이는 이름.</summary>
        public void FromPlaces(IEnumerable<PlaceMemory> places, ZoneXY home, Func<string, string> foeName)
        {
            PlaceMemory died = null, fled = null, beat = null, pit = null;
            foreach (var p in places)
            {
                if (p.Deaths > 0 && (died == null || p.Deaths > died.Deaths)) died = p;
                if (p.Flees > 0 && (fled == null || p.Flees > fled.Flees)) fled = p;
                if (p.Wins > 0 && (beat == null || p.Wins > beat.Wins)) beat = p;
                if ((p.Features & PlaceFeature.Pit) != 0 && pit == null) pit = p;
            }
            if (died != null)
            {
                DeathFoe = Name(died.Killer ?? TopKind(died), foeName);
                DeathDir = DirOf(died.Zone, home);
                Deaths = died.Deaths;
                if (DeathFoe == null) DeathDir = null;
            }
            if (fled != null) FledFoe = Name(TopKind(fled), foeName);
            if (beat != null) BeatenFoe = Name(TopKind(beat), foeName);
            if (pit != null) PitDir = DirOf(pit.Zone, home);
        }

        /// <summary>
        /// 지식에서 아는 자원 하나(많이 아는 5가지 중, 가장 가까운 구역 방향)와 거점과 다른 바이옴 하나(바다 빼고, 가장 가까운 구역 방향)를 뽑는다.
        /// itemName = 프리팹 → 보이는 이름, pick(n) = 0..n-1 중 하나(무작위).
        /// </summary>
        public void FromKnowledge(Knowledge k, ZoneXY home, int homeBiome, Func<string, string> itemName, Func<int, int> pick)
        {
            if (k == null) return;
            var items = k.Summary(home);
            if (items.Count > 0)
            {
                var it = items[pick(Math.Min(5, items.Count))];
                Item = itemName != null ? itemName(it.Item) : it.Item;
                ItemDir = DirOf(it.Nearest, home);
            }
            var nearest = new Dictionary<int, ZoneXY>();
            foreach (var z in k.Zones)
            {
                if (z.Biome == 0 || z.Biome == homeBiome || z.Biome == OceanBit) continue;
                if (!nearest.TryGetValue(z.Biome, out var best) || z.Zone.DistSq(home) < best.DistSq(home)) nearest[z.Biome] = z.Zone;
            }
            if (nearest.Count > 0)
            {
                var biomes = new List<int>(nearest.Keys);
                biomes.Sort();
                int b = biomes[pick(biomes.Count)];
                Biome = BiomeNames.Of(b);
                BiomeDir = DirOf(nearest[b], home);
            }
        }

        const int OceanBit = 256;

        static string TopKind(PlaceMemory p)
        {
            GroupFoe best = null;
            foreach (var g in p.Group) if (best == null || g.Count > best.Count) best = g;
            return best?.Kind;
        }

        static string Name(string kind, Func<string, string> foeName) =>
            string.IsNullOrEmpty(kind) ? null : foeName != null ? foeName(kind) : kind;

        /// <summary>거점 구역에서 본 8방위. 같은 구역이면 "거점 근처".</summary>
        public static string DirOf(ZoneXY z, ZoneXY home) => Bearing.Of(z.X - home.X, z.Y - home.Y) ?? "거점 근처";
    }

    /// <summary>할 말 한 줄: NPC 자리(0 = 1호, 1 = 2호 — Personality 값), Speech 키, 인자, 앞 줄 뒤 몇 초.</summary>
    public sealed class BanterLine
    {
        public int Npc;
        public string Key;
        public Dictionary<string, string> Args;
        public float Delay;
    }

    /// <summary>
    /// 일꾼끼리 잡담 고르기 (사용자 결정 2026-10-23 "잡담"). 토큰 0 — 말은 Speech의 bt.* 키, 내용은 BanterFacts.
    /// 쉴 때 약 2분마다 한 번(둘이 같이 있으면 주고받기, 혼자면 혼잣말), 일이 생기면 상대가 곁에 있을 때 바로 반응하고
    /// 없으면 소식으로 들고 있다가(10분) 다음 잡담 때 먼저 꺼낸다. 언제 말해도 되는지(쉬는 중·서로 곁·플레이어 근처)는 플러그인이 정한다.
    /// </summary>
    public sealed class Banter
    {
        public const double IntervalMin = 100, IntervalMax = 140;
        public const double NewsKeep = 600;
        public const double EventCooldown = 60;
        /// <summary>날씨 반응은 5분에 한 번(너무 자주 말하지 않게, 사용자 10-28).</summary>
        public const double WeatherCooldown = 300;
        /// <summary>반응 바로 뒤 잡담이 겹치지 않게 미루는 시간.</summary>
        public const double AfterEvent = 30;
        const int RecentTopics = 4, MaxNews = 5;

        /// <summary>기억이 있어야 고르는 주제(무게 3)와 늘 고를 수 있는 주제(무게 1).</summary>
        public static readonly string[] MemoryTopics = { "death", "fled", "beaten", "biome", "item", "skill", "crop", "pit", "night" };
        public static readonly string[] AnyTopics = { "gods", "sea", "home", "mead", "work", "food", "old", "tools" };
        /// <summary>잡담 때 플레이어에게 말 걸 확률(대사 늘리기 나), 10-29).</summary>
        public const double PlayerChance = 0.25;

        sealed class News
        {
            public int Subject;
            public BanterEvent Event;
            public Dictionary<string, string> Args;
            public double At;
        }

        readonly Random _rng;
        double _next = -1;
        readonly Dictionary<BanterEvent, double> _lastEvent = new Dictionary<BanterEvent, double>();
        readonly List<News> _news = new List<News>();
        readonly List<string> _recent = new List<string>();

        public Banter(Random rng = null) { _rng = rng ?? new Random(); }

        public static string EventKey(BanterEvent e) => "bt.ev." + e.ToString().ToLowerInvariant();

        static string EventKey(BanterEvent e, IDictionary<string, string> args) =>
            e == BanterEvent.Weather && args != null && args.TryGetValue("kind", out string k) ? EventKey(e) + "." + k : EventKey(e);

        /// <summary>
        /// 날씨 종류: 젖음(EnvSetup.m_isWet)이면 rain(이름에 storm이 있으면 storm), 눈 쌓임(m_snowBuildup &gt; 0)이면 snow,
        /// 그 밖은 앞이 rain·storm·snow였을 때만 clear(갬). 말할 거리가 없으면 null.
        /// </summary>
        public static string WeatherKind(bool wet, bool snow, string name, string prev)
        {
            if (wet) return name != null && name.IndexOf("storm", StringComparison.OrdinalIgnoreCase) >= 0 ? "storm" : "rain";
            if (snow) return "snow";
            return prev == "rain" || prev == "storm" || prev == "snow" ? "clear" : null;
        }

        /// <summary>
        /// 일이 생겼다. subject = 겪은 NPC 자리, args = 그 일의 인자(아래 bt.ev.* 대사가 쓰는 것: haul {item}{n}, skillup {skill}{level}, fled {foe}, biome {biome}).
        /// partnerNear면 상대가 바로 반응하고 겪은 쪽이 받는다. 아니면 소식으로 들고 있는다. 같은 종류는 60초에 한 번.
        /// </summary>
        public List<BanterLine> OnEvent(double now, int subject, BanterEvent e, IDictionary<string, string> args, bool partnerNear, string[] names)
        {
            var lines = new List<BanterLine>();
            bool weather = e == BanterEvent.Weather;
            if (weather && !partnerNear) return lines; // 날씨는 곁에서 같이 볼 때만(지난 날씨는 소식 거리가 아님)
            if (_lastEvent.TryGetValue(e, out double last) && now - last < (weather ? WeatherCooldown : EventCooldown)) return lines;
            _lastEvent[e] = now;
            var copy = args != null ? new Dictionary<string, string>(args) : new Dictionary<string, string>();
            if (!partnerNear)
            {
                _news.Add(new News { Subject = subject, Event = e, Args = copy, At = now });
                if (_news.Count > MaxNews) _news.RemoveAt(0);
                return lines;
            }
            Exchange(lines, subject, EventKey(e, copy), ".react", copy, names);
            if (_next >= 0 && _next < now + AfterEvent) _next = now + AfterEvent;
            return lines;
        }

        /// <summary>잡담 때가 됐는지(처음엔 true — Tick이 첫 때를 잡는다). 기억 모으기(BanterFacts)는 이때만 하면 된다.</summary>
        public bool Due(double now) => _next < 0 || now >= _next;

        /// <summary>시험용: 다음 잡담 때를 정한다.</summary>
        public void SetNext(double at) => _next = at;

        /// <summary>
        /// 매 판단마다 부른다. together = 둘 다 쉬며 곁에 있고 플레이어도 근처, alone = 혼자 쉬며 플레이어 근처인 NPC 자리(-1 없음).
        /// 때가 안 됐거나 말할 상황이 아니면 빈 목록.
        /// </summary>
        public List<BanterLine> Tick(double now, bool together, int alone, BanterFacts facts, string[] names)
        {
            var lines = new List<BanterLine>();
            _news.RemoveAll(n => now - n.At > NewsKeep);
            if (_next < 0) _next = now + Interval();
            if (now < _next) return lines;
            if (!together && alone < 0) return lines;
            _next = now + Interval();
            if (together)
            {
                if (_news.Count > 0)
                {
                    var n = _news[0];
                    _news.RemoveAt(0);
                    Exchange(lines, n.Subject, EventKey(n.Event, n.Args), ".news", n.Args, names);
                    return lines;
                }
                if (ToPlayer(lines, _rng.Next(2), true, facts, names)) return lines;
                string topic = PickTopic(facts);
                int opener = _rng.Next(2);
                lines.Add(Line(opener, "bt." + topic + ".open", TopicArgs(topic, facts, opener, names), 0f));
                lines.Add(Line(1 - opener, "bt." + topic + ".reply", TopicArgs(topic, facts, 1 - opener, names), Gap()));
                return lines;
            }
            if (ToPlayer(lines, alone, false, facts, names)) return lines;
            lines.Add(Line(alone, facts != null && facts.Night ? "bt.solo.night" : "bt.solo", new Dictionary<string, string> { ["partner"] = names[1 - alone] }, 0f));
            return lines;
        }

        /// <summary>가끔(PlayerChance) 곁의 플레이어에게 말을 건다(밤이면 밤 대사). 둘이면 상대가 받는다.</summary>
        bool ToPlayer(List<BanterLine> lines, int sp, bool together, BanterFacts f, string[] names)
        {
            if (f == null || f.Player == null || _rng.NextDouble() >= PlayerChance) return false;
            lines.Add(Line(sp, f.Night ? "bt.player.night" : "bt.player.open", new Dictionary<string, string> { ["player"] = f.Player, ["partner"] = names[1 - sp] }, 0f));
            if (together) lines.Add(Line(1 - sp, "bt.player.reply", new Dictionary<string, string> { ["player"] = f.Player, ["partner"] = names[sp] }, Gap()));
            return true;
        }

        /// <summary>상대(1 - subject)가 먼저 말하고(react·news, {who} = 겪은 쪽), 겪은 쪽이 받는다(self, {partner} = 상대).</summary>
        void Exchange(List<BanterLine> lines, int subject, string key, string first, Dictionary<string, string> args, string[] names)
        {
            int other = 1 - subject;
            lines.Add(Line(other, key + first, new Dictionary<string, string>(args) { ["who"] = names[subject], ["partner"] = names[subject] }, 0f));
            lines.Add(Line(subject, key + ".self", new Dictionary<string, string>(args) { ["who"] = names[subject], ["partner"] = names[other] }, Gap()));
        }

        string PickTopic(BanterFacts f)
        {
            var pool = new List<string>();
            foreach (var t in MemoryTopics)
                if (!_recent.Contains(t) && Has(t, f)) { pool.Add(t); pool.Add(t); pool.Add(t); }
            foreach (var t in AnyTopics)
                if (!_recent.Contains(t)) pool.Add(t);
            if (pool.Count == 0)
                foreach (var t in AnyTopics)
                    if (_recent.Count == 0 || t != _recent[_recent.Count - 1]) pool.Add(t);
            string topic = pool[_rng.Next(pool.Count)];
            _recent.Add(topic);
            if (_recent.Count > RecentTopics) _recent.RemoveAt(0);
            return topic;
        }

        public static bool Has(string topic, BanterFacts f)
        {
            if (f == null) return false;
            switch (topic)
            {
                case "death": return f.DeathFoe != null && f.Deaths > 0;
                case "fled": return f.FledFoe != null;
                case "beaten": return f.BeatenFoe != null;
                case "biome": return f.Biome != null && f.BiomeDir != null;
                case "item": return f.Item != null && f.ItemDir != null;
                case "skill": return f.Skill[0] != null && f.Skill[1] != null;
                case "crop": return f.Crop != null;
                case "pit": return f.PitDir != null;
                case "night": return f.Night;
                default: return true;
            }
        }

        /// <summary>주제 대사 인자. {partner}는 말하는 쪽에서 본 상대, {skill}{level}은 말하는 쪽 솜씨, {pskill}{plevel}은 상대 솜씨.</summary>
        static Dictionary<string, string> TopicArgs(string topic, BanterFacts f, int speaker, string[] names)
        {
            var a = new Dictionary<string, string> { ["partner"] = names[1 - speaker] };
            if (f == null) return a;
            switch (topic)
            {
                case "death": a["foe"] = f.DeathFoe; a["dir"] = f.DeathDir; a["deaths"] = f.Deaths.ToString(); break;
                case "fled": a["foe"] = f.FledFoe; break;
                case "beaten": a["foe"] = f.BeatenFoe; break;
                case "biome": a["biome"] = f.Biome; a["dir"] = f.BiomeDir; break;
                case "item": a["item"] = f.Item; a["dir"] = f.ItemDir; break;
                case "skill":
                    a["skill"] = f.Skill[speaker]; a["level"] = f.Level[speaker].ToString();
                    a["pskill"] = f.Skill[1 - speaker]; a["plevel"] = f.Level[1 - speaker].ToString();
                    break;
                case "crop": a["crop"] = f.Crop; break;
                case "pit": a["dir"] = f.PitDir; break;
            }
            return a;
        }

        static BanterLine Line(int npc, string key, Dictionary<string, string> args, float delay) =>
            new BanterLine { Npc = npc, Key = key, Args = args, Delay = delay };

        double Interval() => IntervalMin + _rng.NextDouble() * (IntervalMax - IntervalMin);
        float Gap() => 2.5f + (float)_rng.NextDouble() * 1.5f;
    }
}
