using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ValheimHelper.Core.Work;

namespace ValheimHelper.Core.Npc
{
    /// <summary>한 번의 마주침이 어떻게 끝났는지.</summary>
    public enum Outcome { Won = 0, Back = 1, Fled = 2, Died = 3 }

    /// <summary>장소의 특이점(지금 코드가 감지하는 것만).</summary>
    [Flags]
    public enum PlaceFeature { None = 0, DeepWater = 1, Cold = 2, Pit = 4 }

    /// <summary>무리 안의 적 한 종류: 동시에 본 수와 가장 높은 레벨(별 0 = 1).</summary>
    public sealed class GroupFoe
    {
        public string Kind;
        public int Count;
        public int Level = 1;
        public bool Boss;
    }

    public sealed class PlaceMemory
    {
        public ZoneXY Zone;
        /// <summary>이곳에서 본 가장 무거운 무리(감쇠 반영해 바꿈).</summary>
        public readonly List<GroupFoe> Group = new List<GroupFoe>();
        public double GroupAt;
        public int Wins, Backs, Flees, Deaths;
        public Outcome LastOutcome;
        public double LastAt;
        /// <summary>마지막 죽음 시각. 죽은 적 없으면 -1.</summary>
        public double LastDeathAt = -1;
        public string Killer;
        public PlaceFeature Features;
        /// <summary>마지막으로 기록을 건드린 시각(100곳 넘칠 때 오래된 곳부터 지움).</summary>
        public double TouchedAt;
    }

    /// <summary>판단용: 한 장소의 지금 위험.</summary>
    public sealed class PlaceRisk
    {
        /// <summary>감쇠까지 반영한 잃을 체력 비율(옆 구역은 절반).</summary>
        public float Ratio;
        /// <summary>죽음 기록의 무게(0~1).</summary>
        public float DeathWeight;
        /// <summary>대표 적(가장 위협적인 종류). 없으면 null.</summary>
        public string TopFoe;
        /// <summary>무게가 남은 기록에 보스가 있다.</summary>
        public bool Boss;
        public PlaceFeature Features;
        /// <summary>이 장소(옆 구역 포함)에 기록이 하나라도 있다.</summary>
        public bool Known;
    }

    /// <summary>
    /// 장소 기억 (구상서 8-1장, 설계 docs/valheim/design-depart-craft.txt A장). 위험한 일이 있었던 구역만 100곳까지.
    /// 위험도는 숫자로 두지 않고 적 무리를 두었다가, 판단할 때 지금 내 상태로 계산한다(장비가 좋아지면 저절로 덜 위험).
    /// 시간 감쇠: 무리 3 게임일, 죽음 20 게임일 반감기. 두 NPC가 같이 쓰고 월드에 저장한다.
    /// </summary>
    public sealed class PlaceBook
    {
        public const int MaxPlaces = 100;
        public const double Day = 1800.0;
        public const double HalfLife = 3 * Day, DeathHalfLife = 20 * Day;
        /// <summary>지울 곳 고를 때 죽음이 있는 곳에 더해 주는 시간(오래 남김).</summary>
        public const double DeathKeep = 20 * Day;
        public const float NeighborShare = 0.5f;
        /// <summary>무리 무게를 비교할 때 쓰는 기준 전사.</summary>
        const float RefHealth = 100f, RefHit = 20f;

        readonly Dictionary<ZoneXY, PlaceMemory> _places = new Dictionary<ZoneXY, PlaceMemory>();
        public int Count => _places.Count;
        public bool Dirty;

        public PlaceMemory Get(ZoneXY z) => _places.TryGetValue(z, out var m) ? m : null;
        public IEnumerable<PlaceMemory> All => _places.Values;

        public static float Weight(double since, double halfLife) => since <= 0 ? 1f : (float)Math.Pow(0.5, since / halfLife);

        /// <summary>무리를 판단용 적 목록으로 편다. resolve: 종류·레벨 하나의 능력치(체력·한 방).</summary>
        public static List<Foe> Expand(IList<GroupFoe> group, Func<GroupFoe, Foe> resolve)
        {
            var list = new List<Foe>();
            foreach (var g in group)
            {
                Foe one = resolve(g);
                for (int i = 0; i < g.Count; i++)
                    list.Add(new Foe { Kind = g.Kind, Health = one.Health, Damage = one.Damage, Boss = g.Boss || one.Boss });
            }
            return list;
        }

        static float RefLoss(IList<GroupFoe> group, Func<GroupFoe, Foe> resolve) =>
            group.Count == 0 ? 0f : Danger.Ratio(Expand(group, resolve), RefHealth, RefHit, d => d);

        PlaceMemory Touch(ZoneXY z, double now)
        {
            if (!_places.TryGetValue(z, out var m)) { m = new PlaceMemory { Zone = z }; _places[z] = m; }
            m.TouchedAt = now;
            Dirty = true;
            return m;
        }

        /// <summary>마주침 하나를 적는다. 무리는 옛 무리(감쇠 반영)보다 무거우면 바꾼다.</summary>
        public void Record(ZoneXY z, IList<GroupFoe> group, Outcome outcome, double now, Func<GroupFoe, Foe> resolve, string killer = null)
        {
            var m = Touch(z, now);
            if (group != null && group.Count > 0)
            {
                float old = RefLoss(m.Group, resolve) * Weight(now - m.GroupAt, HalfLife);
                if (m.Group.Count == 0 || RefLoss(group, resolve) >= old)
                {
                    m.Group.Clear();
                    foreach (var g in group)
                        if (g.Count > 0 && !string.IsNullOrEmpty(g.Kind))
                            m.Group.Add(new GroupFoe { Kind = g.Kind, Count = g.Count, Level = Math.Max(1, g.Level), Boss = g.Boss });
                    m.GroupAt = now;
                }
            }
            switch (outcome)
            {
                case Outcome.Won: m.Wins++; break;
                case Outcome.Back: m.Backs++; break;
                case Outcome.Fled: m.Flees++; break;
                case Outcome.Died: m.Deaths++; m.LastDeathAt = now; m.Killer = killer; break;
            }
            m.LastOutcome = outcome;
            m.LastAt = now;
            Trim();
        }

        public void AddFeature(ZoneXY z, PlaceFeature f, double now)
        {
            if (_places.TryGetValue(z, out var m) && (m.Features & f) == f) return;
            Touch(z, now).Features |= f;
            Trim();
        }

        void Trim()
        {
            while (_places.Count > MaxPlaces)
            {
                PlaceMemory oldest = null;
                double oldestKey = double.MaxValue;
                foreach (var m in _places.Values)
                {
                    double key = m.TouchedAt + (m.Deaths > 0 ? DeathKeep : 0);
                    if (key < oldestKey) { oldestKey = key; oldest = m; }
                }
                _places.Remove(oldest.Zone);
            }
        }

        /// <summary>
        /// 지금 위험. ratio: 적 목록 → 지금 내 상태로 잃을 체력 비율(Danger.Ratio에 내 체력·한 방·갑옷을 넣은 것).
        /// 목표 구역과 옆 8구역을 보고 옆은 절반으로 센다. 죽음 무게는 목표 구역만.
        /// </summary>
        public PlaceRisk Risk(ZoneXY z, double now, Func<IList<Foe>, float> ratio, Func<GroupFoe, Foe> resolve)
        {
            var r = new PlaceRisk();
            float topScore = 0f;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!_places.TryGetValue(new ZoneXY(z.X + dx, z.Y + dy), out var m)) continue;
                    r.Known = true;
                    bool own = dx == 0 && dy == 0;
                    if (own)
                    {
                        r.Features = m.Features;
                        if (m.LastDeathAt >= 0) r.DeathWeight = Weight(now - m.LastDeathAt, DeathHalfLife);
                    }
                    if (m.Group.Count == 0) continue;
                    float w = Weight(now - m.GroupAt, HalfLife) * (own ? 1f : NeighborShare);
                    var foes = Expand(m.Group, resolve);
                    float v = ratio(foes) * w;
                    if (v > r.Ratio) r.Ratio = v;
                    foreach (var g in m.Group)
                    {
                        if (g.Boss && w >= 0.25f) r.Boss = true;
                        Foe one = resolve(g);
                        float score = one.Damage * g.Count * w;
                        if (score > topScore) { topScore = score; r.TopFoe = g.Kind; }
                    }
                }
            return r;
        }

        /// <summary>
        /// 그 바이옴 땅 자체의 위험(⑫ 다, 구상서 7-1 "이 장비론 평원 무리예요"): 그 바이옴 구역(biomeOf)에서 겪은 적 종류 하나씩(보스 빼고)을
        /// 지금 내 상태로 셈한 비율 중 가장 큰 것. 그 바이옴 어디서나 나오는 적이라 감쇠하지 않는다. 겪은 적이 없으면 0.
        /// native(종류, 바이옴): 그 바이옴에 나는 적인지(게임 생성 표). null이면 다 셈.
        /// </summary>
        public PlaceRisk BiomeRisk(int biome, Func<ZoneXY, int> biomeOf, Func<IList<Foe>, float> ratio, Func<GroupFoe, Foe> resolve, Func<string, int, bool> native = null)
        {
            var r = new PlaceRisk();
            if (biome == 0) return r;
            var one = new List<Foe>(1) { null };
            foreach (var m in _places.Values)
            {
                if (m.Group.Count == 0 || biomeOf(m.Zone) != biome) continue;
                r.Known = true;
                foreach (var g in m.Group)
                {
                    if (g.Boss || native != null && !native(g.Kind, biome)) continue; // 떠돌다 넘어온 다른 바이옴 적(초원의 트롤)은 그 땅 몫이 아님
                    Foe f = resolve(g);
                    if (f.Boss) continue;
                    one[0] = new Foe { Kind = g.Kind, Health = f.Health, Damage = f.Damage };
                    float v = ratio(one);
                    if (v > r.Ratio) { r.Ratio = v; r.TopFoe = g.Kind; }
                }
            }
            return r;
        }

        /// <summary>!기억: 지금 위험(무리 감쇠·죽음 무게 중 큰 쪽) 높은 순.</summary>
        public List<PlaceMemory> Top(int n, double now, Func<IList<Foe>, float> ratio, Func<GroupFoe, Foe> resolve)
        {
            var scored = new List<KeyValuePair<float, PlaceMemory>>();
            foreach (var m in _places.Values)
            {
                float s = m.Group.Count > 0 ? ratio(Expand(m.Group, resolve)) * Weight(now - m.GroupAt, HalfLife) : 0f;
                if (m.LastDeathAt >= 0) s = Math.Max(s, Weight(now - m.LastDeathAt, DeathHalfLife));
                if (s <= 0.01f && m.Features == PlaceFeature.None) continue;
                scored.Add(new KeyValuePair<float, PlaceMemory>(s, m));
            }
            scored.Sort((a, b) => b.Key.CompareTo(a.Key));
            var list = new List<PlaceMemory>();
            for (int i = 0; i < scored.Count && i < n; i++) list.Add(scored[i].Value);
            return list;
        }

        // ---- 저장: 한 줄에 한 곳 "x,y,touched,groupAt,wins,backs,flees,deaths,last,lastAt,lastDeathAt,features,killer|kind=count:level:boss;…" ----

        static string N(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

        public string Save()
        {
            var sb = new StringBuilder();
            foreach (var m in _places.Values)
            {
                sb.Append(m.Zone.X).Append(',').Append(m.Zone.Y).Append(',').Append(N(m.TouchedAt)).Append(',').Append(N(m.GroupAt)).Append(',')
                  .Append(m.Wins).Append(',').Append(m.Backs).Append(',').Append(m.Flees).Append(',').Append(m.Deaths).Append(',')
                  .Append((int)m.LastOutcome).Append(',').Append(N(m.LastAt)).Append(',').Append(N(m.LastDeathAt)).Append(',')
                  .Append((int)m.Features).Append(',').Append(Clean(m.Killer)).Append('|');
                for (int i = 0; i < m.Group.Count; i++)
                {
                    var g = m.Group[i];
                    if (i > 0) sb.Append(';');
                    sb.Append(Clean(g.Kind)).Append('=').Append(g.Count).Append(':').Append(g.Level).Append(':').Append(g.Boss ? '1' : '0');
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        static string Clean(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace(",", "").Replace("|", "").Replace(";", "").Replace("=", "").Replace("\n", "");

        public static PlaceBook Load(string text)
        {
            var b = new PlaceBook();
            if (string.IsNullOrEmpty(text)) return b;
            var inv = CultureInfo.InvariantCulture;
            foreach (var line in text.Split('\n'))
            {
                int bar = line.IndexOf('|');
                if (bar < 0) continue;
                var h = line.Substring(0, bar).Split(',');
                if (h.Length < 13
                    || !int.TryParse(h[0], NumberStyles.Integer, inv, out int x)
                    || !int.TryParse(h[1], NumberStyles.Integer, inv, out int y)) continue;
                var m = new PlaceMemory { Zone = new ZoneXY(x, y) };
                double.TryParse(h[2], NumberStyles.Float, inv, out m.TouchedAt);
                double.TryParse(h[3], NumberStyles.Float, inv, out m.GroupAt);
                int.TryParse(h[4], NumberStyles.Integer, inv, out m.Wins);
                int.TryParse(h[5], NumberStyles.Integer, inv, out m.Backs);
                int.TryParse(h[6], NumberStyles.Integer, inv, out m.Flees);
                int.TryParse(h[7], NumberStyles.Integer, inv, out m.Deaths);
                if (int.TryParse(h[8], NumberStyles.Integer, inv, out int last)) m.LastOutcome = (Outcome)last;
                double.TryParse(h[9], NumberStyles.Float, inv, out m.LastAt);
                if (!double.TryParse(h[10], NumberStyles.Float, inv, out m.LastDeathAt)) m.LastDeathAt = -1;
                if (int.TryParse(h[11], NumberStyles.Integer, inv, out int f)) m.Features = (PlaceFeature)f;
                m.Killer = h[12].Length > 0 ? h[12] : null;
                foreach (var part in line.Substring(bar + 1).Split(';'))
                {
                    int eq = part.IndexOf('=');
                    if (eq <= 0) continue;
                    var v = part.Substring(eq + 1).Split(':');
                    if (v.Length != 3 || !int.TryParse(v[0], NumberStyles.Integer, inv, out int count)
                        || !int.TryParse(v[1], NumberStyles.Integer, inv, out int level)) continue;
                    m.Group.Add(new GroupFoe { Kind = part.Substring(0, eq), Count = count, Level = level, Boss = v[2] == "1" });
                }
                b._places[m.Zone] = m;
            }
            b.Trim();
            b.Dirty = false;
            return b;
        }
    }
}
