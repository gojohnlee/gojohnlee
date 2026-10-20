using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ValheimHelper.Core.Work
{
    /// <summary>구역 좌표(게임의 64m 구역). ValueTuple은 게임 런타임에 없어서 쓰지 않는다.</summary>
    public struct ZoneXY : IEquatable<ZoneXY>
    {
        public readonly int X, Y;
        public ZoneXY(int x, int y) { X = x; Y = y; }
        public bool Equals(ZoneXY o) => X == o.X && Y == o.Y;
        public override bool Equals(object obj) => obj is ZoneXY o && Equals(o);
        public override int GetHashCode() => X * 73856093 ^ Y * 19349663;
        public override string ToString() => X + "," + Y;
        /// <summary>구역 칸 수로 잰 거리(체비쇼프).</summary>
        public int Ring(ZoneXY o) => Math.Max(Math.Abs(X - o.X), Math.Abs(Y - o.Y));
        public int DistSq(ZoneXY o) => (X - o.X) * (X - o.X) + (Y - o.Y) * (Y - o.Y);
    }

    public sealed class ZoneMemory
    {
        public ZoneXY Zone;
        /// <summary>물건 프리팹 이름 → 마지막으로 본 자원 수(나무 그루 수 등).</summary>
        public readonly Dictionary<string, int> Items = new Dictionary<string, int>();
        /// <summary>구역을 직접 둘러봤는지(다시 뒤지지 않는다, 구상서 8-2장).</summary>
        public bool Looked;
        public double SeenAt;
        /// <summary>가 본 바이옴(게임 Heightmap.Biome 값). 0 = 모름.</summary>
        public int Biome;
    }

    /// <summary>!지식 대답용: 물건 하나에 대해 아는 것.</summary>
    public sealed class ItemKnowledge
    {
        public string Item;
        /// <summary>있다고 기억하는 구역 수.</summary>
        public int Zones;
        /// <summary>본 자원 수 합(나무 그루 수 등).</summary>
        public int Total;
        public ZoneXY Nearest;
        /// <summary>본 바이옴들(비트 합).</summary>
        public int Biomes;
    }

    /// <summary>
    /// 장소 지식 (구상서 8-2장): NPC가 직접 본 자원만 구역별로 기억한다. 두 NPC가 같이 쓰고 월드에 저장한다.
    /// </summary>
    public sealed class Knowledge
    {
        readonly Dictionary<ZoneXY, ZoneMemory> _zones = new Dictionary<ZoneXY, ZoneMemory>();
        public int Count => _zones.Count;
        /// <summary>저장할 게 바뀌었는지(게임 쪽이 가끔 저장한다).</summary>
        public bool Dirty;

        public ZoneMemory Get(ZoneXY z) => _zones.TryGetValue(z, out var m) ? m : null;

        ZoneMemory GetOrAdd(ZoneXY z)
        {
            if (!_zones.TryGetValue(z, out var m)) { m = new ZoneMemory { Zone = z }; _zones[z] = m; }
            return m;
        }

        /// <summary>
        /// 한 번 둘러본 결과. seen: 구역 → (물건 → 이번에 본 수). fullyVisible: 이번에 거의 다 보인 구역 —
        /// 거기서 안 보인 물건은 다 쓴 것으로 지운다. 일부만 보인 구역은 이전 기억과 큰 쪽을 남긴다.
        /// </summary>
        public void Saw(IDictionary<ZoneXY, Dictionary<string, int>> seen, ICollection<ZoneXY> fullyVisible, double now)
        {
            foreach (var z in fullyVisible)
            {
                var m = GetOrAdd(z);
                seen.TryGetValue(z, out var counts);
                var gone = new List<string>();
                foreach (var item in m.Items.Keys)
                    if (counts == null || !counts.ContainsKey(item)) gone.Add(item);
                foreach (var item in gone) m.Items.Remove(item);
                if (counts != null) foreach (var kv in counts) m.Items[kv.Key] = kv.Value;
                m.SeenAt = now;
                Dirty = true;
            }
            foreach (var kv in seen)
            {
                if (fullyVisible.Contains(kv.Key)) continue;
                var m = GetOrAdd(kv.Key);
                foreach (var c in kv.Value)
                    if (!m.Items.TryGetValue(c.Key, out int old) || old < c.Value || now - m.SeenAt > 600) m.Items[c.Key] = c.Value;
                m.SeenAt = now;
                Dirty = true;
            }
        }

        public void MarkLooked(ZoneXY z)
        {
            var m = GetOrAdd(z);
            if (!m.Looked) { m.Looked = true; Dirty = true; }
        }

        public bool IsLooked(ZoneXY z) => _zones.TryGetValue(z, out var m) && m.Looked;

        public int LookedCount
        {
            get
            {
                int n = 0;
                foreach (var m in _zones.Values) if (m.Looked) n++;
                return n;
            }
        }

        /// <summary>그 구역에 가 봐서 바이옴을 안다.</summary>
        public void SetBiome(ZoneXY z, int biome)
        {
            if (biome == 0) return;
            var m = GetOrAdd(z);
            if (m.Biome != biome) { m.Biome = biome; Dirty = true; }
        }

        public int BiomeOf(ZoneXY z) => _zones.TryGetValue(z, out var m) ? m.Biome : 0;

        /// <summary>그 바이옴(비트 합 중 하나라도)에 가 본 적이 있는지.</summary>
        public bool KnowsBiome(int biome)
        {
            foreach (var m in _zones.Values) if ((m.Biome & biome) != 0) return true;
            return false;
        }

        /// <summary>그 물건을 본 바이옴들(비트 합). "구리는 검은 숲".</summary>
        public int BiomesOf(string item)
        {
            int mask = 0;
            foreach (var m in _zones.Values)
                if (m.Items.TryGetValue(item, out int n) && n > 0) mask |= m.Biome;
            return mask;
        }

        /// <summary>아는 물건들: 구역 수 많은 순(같으면 본 수 많은 순), from에서 가장 가까운 구역과 바이옴.</summary>
        public List<ItemKnowledge> Summary(ZoneXY from)
        {
            var map = new Dictionary<string, ItemKnowledge>();
            foreach (var m in _zones.Values)
                foreach (var kv in m.Items)
                {
                    if (kv.Value <= 0) continue;
                    if (!map.TryGetValue(kv.Key, out var ik)) map[kv.Key] = ik = new ItemKnowledge { Item = kv.Key, Nearest = m.Zone };
                    else if (m.Zone.DistSq(from) < ik.Nearest.DistSq(from)) ik.Nearest = m.Zone;
                    ik.Zones++;
                    ik.Total += kv.Value;
                    ik.Biomes |= m.Biome;
                }
            var list = new List<ItemKnowledge>(map.Values);
            list.Sort((a, b) => a.Zones != b.Zones ? b.Zones.CompareTo(a.Zones) : a.Total != b.Total ? b.Total.CompareTo(a.Total) : string.CompareOrdinal(a.Item, b.Item));
            return list;
        }

        /// <summary>그 물건이 있다고 기억하는 구역들, from에서 가까운 순.</summary>
        public List<ZoneMemory> ZonesWith(string item, ZoneXY from)
        {
            var list = new List<ZoneMemory>();
            foreach (var m in _zones.Values)
                if (m.Items.TryGetValue(item, out int n) && n > 0) list.Add(m);
            list.Sort((a, b) => a.Zone.DistSq(from).CompareTo(b.Zone.DistSq(from)));
            return list;
        }

        /// <summary>그 구역의 그 물건을 다 썼다(가 봤는데 없었다).</summary>
        public void Forget(ZoneXY z, string item)
        {
            if (_zones.TryGetValue(z, out var m) && m.Items.Remove(item)) Dirty = true;
        }

        // ---- 저장: "x,y,looked,seenAt;item=n;item=n" 줄들 ----

        public string Save()
        {
            var sb = new StringBuilder();
            foreach (var m in _zones.Values)
            {
                sb.Append(m.Zone.X).Append(',').Append(m.Zone.Y).Append(',').Append(m.Looked ? '1' : '0').Append(',')
                  .Append(m.SeenAt.ToString("0", CultureInfo.InvariantCulture)).Append(',').Append(m.Biome);
                foreach (var kv in m.Items) sb.Append(';').Append(kv.Key).Append('=').Append(kv.Value);
                sb.Append('\n');
            }
            return sb.ToString();
        }

        public static Knowledge Load(string text)
        {
            var k = new Knowledge();
            if (string.IsNullOrEmpty(text)) return k;
            foreach (var line in text.Split('\n'))
            {
                if (line.Length == 0) continue;
                var parts = line.Split(';');
                var head = parts[0].Split(',');
                if (head.Length < 4
                    || !int.TryParse(head[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
                    || !int.TryParse(head[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y)) continue;
                var m = k.GetOrAdd(new ZoneXY(x, y));
                m.Looked = head[2] == "1";
                double.TryParse(head[3], NumberStyles.Float, CultureInfo.InvariantCulture, out m.SeenAt);
                if (head.Length >= 5) int.TryParse(head[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out m.Biome); // 0.3.6부터
                for (int i = 1; i < parts.Length; i++)
                {
                    int eq = parts[i].LastIndexOf('=');
                    if (eq > 0 && int.TryParse(parts[i].Substring(eq + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                        m.Items[parts[i].Substring(0, eq)] = n;
                }
            }
            return k;
        }
    }

    /// <summary>모르는 자원을 찾아다닐 다음 구역 (구상서 8-2장: 거점 가까운 곳부터, 둘러본 곳은 건너뜀).</summary>
    public static class SearchPlan
    {
        /// <param name="explored">지도에서 밝혀진 구역인지(공유 지도의 밝혀진 곳만 다닌다).</param>
        /// <returns>다음 구역. 더 없으면 false.</returns>
        public static bool Next(ZoneXY home, ZoneXY from, int maxRing, Knowledge k, Func<ZoneXY, bool> explored, out ZoneXY next) =>
            Next(home, from, maxRing, k, explored, null, out next);

        /// <param name="reachable">지금 자리에서 걸어서 닿는지(ExplorePlan.Next와 같게 좋은 후보부터 몇 개까지만). null이면 안 본다.</param>
        public static bool Next(ZoneXY home, ZoneXY from, int maxRing, Knowledge k, Func<ZoneXY, bool> explored,
            Func<ZoneXY, bool> reachable, out ZoneXY next)
        {
            var cands = new List<KeyValuePair<double, ZoneXY>>();
            for (int dy = -maxRing; dy <= maxRing; dy++)
                for (int dx = -maxRing; dx <= maxRing; dx++)
                {
                    var z = new ZoneXY(home.X + dx, home.Y + dy);
                    if (k.IsLooked(z) || !explored(z)) continue;
                    cands.Add(new KeyValuePair<double, ZoneXY>(z.Ring(home) * 1e7 + z.DistSq(from), z)); // 거점 가까운 고리 → 지금 자리 가까운 순
                }
            return ExplorePlan.Pick(cands, reachable, out next);
        }
    }
}
