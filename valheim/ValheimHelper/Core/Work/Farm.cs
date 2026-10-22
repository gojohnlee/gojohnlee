using System;
using System.Collections.Generic;

namespace ValheimHelper.Core.Work
{
    /// <summary>
    /// 작물 하나(바닐라 Plant 조각 = 새싹 프리팹). 값은 플러그인이 게임 데이터에서 읽어 채운다:
    /// Piece.m_resources(씨앗), Plant.m_biome·m_growRadius·m_growTime·m_growTimeMax·m_tolerateCold/Heat, 자란 Pickable(m_itemPrefab·m_amount).
    /// </summary>
    public sealed class Crop
    {
        public string Sapling;      // 심을 조각 프리팹(sapling_carrot)
        public string Seed;         // 심을 때 드는 물건(CarrotSeeds)
        public int SeedCost = 1;
        public string Produce;      // 거두면 나오는 물건(Carrot)
        public string Grown;        // 다 자란 Pickable 프리팹(Pickable_Carrot)
        public int Yield = 1;
        public int Biomes;          // Heightmap.Biome 비트
        public bool TolerateCold, TolerateHeat;
        public float GrowRadius = 0.5f;
        public float GrowMin, GrowMax;  // 초(ZNet 시각)
    }

    public sealed class FarmPlan
    {
        /// <summary>새싹 프리팹 → 심을 수.</summary>
        public readonly List<Ingredient> Plant = new List<Ingredient>();
        /// <summary>원한 물건 중 심을 수 없는 것(작물 없음·바이옴·씨앗 없음) → 모자란 수.</summary>
        public readonly List<Ingredient> Missing = new List<Ingredient>();
        public int Count(string sapling)
        {
            foreach (var p in Plant) if (p.Item == sapling) return p.Amount;
            return 0;
        }
    }

    /// <summary>
    /// 농사 판단(구상서 6-3, ⑨). 밭 = 말한 사람이 선 곳 중심 반경(기본 10m, 최대 20m), 평탄화 없음.
    /// 바닐라 규칙: 자라려면 바이옴(m_biome)·경작지·지붕 없음·m_growRadius 안에 다른 물체 없음(Plant.HaveGrowSpace),
    /// 산·딥노스는 m_tolerateCold, 애쉬랜드는 m_tolerateHeat 없으면 못 자람(보호막 안 제외 — 여기선 안 셈).
    /// 자라는 시간 = lerp(m_growTime, m_growTimeMax, 씨 값) 초.
    /// </summary>
    public static class Farm
    {
        public const float DefaultRadius = 10f;
        public const float MaxRadius = 20f;
        /// <summary>씨앗이 이만큼 아래로 줄면 수확물 일부를 다시 심어 씨앗을 얻는다(구상서 6-3).</summary>
        public const int SeedKeep = 10;
        /// <summary>[기본안] 간격 여유: 바닐라는 m_growRadius 구 안에 다른 충돌체가 있으면 못 자람 → 반경 + 여유.</summary>
        public const float SpacingMargin = 0.4f;

        public const int Mountain = 4, AshLands = 32, DeepNorth = 64;

        public static float ClampRadius(float r)
        {
            if (float.IsNaN(r) || r <= 0f) return DefaultRadius;
            return Math.Min(r, MaxRadius);
        }

        public static float Spacing(float growRadius) => Math.Max(growRadius, 0.1f) + SpacingMargin;

        public static bool CanGrow(Crop c, int biome)
        {
            if (c == null || (c.Biomes & biome) == 0) return false;
            if ((biome == Mountain || biome == DeepNorth) && !c.TolerateCold) return false;
            if (biome == AshLands && !c.TolerateHeat) return false;
            return true;
        }

        /// <summary>
        /// 밭 안 심을 자리(가로세로 spacing 칸). 가장자리는 spacing/2 안쪽. 줄마다 방향을 바꿔(뱀 꼴) 걷는 길이 짧게.
        /// </summary>
        public static List<SpotXZ> Spots(float cx, float cz, float radius, float spacing)
        {
            var list = new List<SpotXZ>();
            if (spacing <= 0f) return list;
            radius = ClampRadius(radius);
            float inner = radius - spacing * 0.5f;
            if (inner < 0f) return list;
            int n = (int)Math.Floor(inner / spacing);
            for (int j = -n; j <= n; j++)
            {
                bool back = ((j + n) & 1) == 1;
                for (int k = -n; k <= n; k++)
                {
                    int i = back ? -k : k;
                    float dx = i * spacing, dz = j * spacing;
                    if (dx * dx + dz * dz <= inner * inner) list.Add(new SpotXZ(cx + dx, cz + dz));
                }
            }
            return list;
        }

        /// <summary>
        /// 무엇을 몇 개 심을지. want = 모자란 물건(수확물 기준), stock = 상자 재고(씨앗·수확물), slots = 빈 자리 수.
        /// 1) 원한 것: 그 물건이 나오는 작물(많이 나오는 것 먼저)을 씨앗·자리만큼. 2) 씨앗 유지: 쓴 씨앗이 SeedKeep 아래로 남으면
        /// 그 씨앗이 나오는 작물(예: 당근 → 당근 씨앗)을 남은 자리에 심는다. 씨앗과 수확물이 같은 작물(보리·아마)은 거두면 다시 씨앗이라 안 함.
        /// </summary>
        public static FarmPlan Plan(IList<Crop> crops, int biome, int slots, Func<string, int> stock, IList<Ingredient> want)
        {
            var plan = new FarmPlan();
            var used = new Dictionary<string, int>(StringComparer.Ordinal);
            int Left(string item) => Math.Max(0, (stock?.Invoke(item) ?? 0) - (used.TryGetValue(item, out var u) ? u : 0));
            void Use(string item, int n) => used[item] = (used.TryGetValue(item, out var u) ? u : 0) + n;
            void Add(string sapling, int n)
            {
                foreach (var p in plan.Plant) if (p.Item == sapling) { p.Amount += n; return; }
                plan.Plant.Add(new Ingredient(sapling, n));
            }
            Crop Source(string produce)
            {
                Crop best = null;
                if (crops != null)
                    foreach (var c in crops)
                        if (c != null && c.Produce == produce && CanGrow(c, biome) && (best == null || c.Yield > best.Yield)) best = c;
                return best;
            }
            int Plantable(Crop c) => Math.Min(slots, Left(c.Seed) / Math.Max(1, c.SeedCost));

            var planted = new List<Crop>();
            if (want != null)
                foreach (var w in want)
                {
                    if (w == null || w.Amount <= 0) continue;
                    var c = Source(w.Item);
                    int need = c == null ? 0 : (w.Amount + Math.Max(1, c.Yield) - 1) / Math.Max(1, c.Yield);
                    int n = c == null ? 0 : Math.Min(need, Plantable(c));
                    if (n > 0)
                    {
                        Add(c.Sapling, n);
                        Use(c.Seed, n * Math.Max(1, c.SeedCost));
                        slots -= n;
                        if (!planted.Contains(c)) planted.Add(c);
                    }
                    int short_ = w.Amount - n * (c == null ? 0 : Math.Max(1, c.Yield));
                    if (short_ > 0) plan.Missing.Add(new Ingredient(w.Item, short_));
                }

            foreach (var c in planted)
            {
                if (slots <= 0) break;
                int seeds = Left(c.Seed);
                if (seeds >= SeedKeep) continue;
                var s = Source(c.Seed);
                if (s == null || s == c || s.Seed == c.Seed) continue;
                int need = (SeedKeep - seeds + Math.Max(1, s.Yield) - 1) / Math.Max(1, s.Yield);
                int n = Math.Min(need, Plantable(s));
                if (n <= 0) continue;
                Add(s.Sapling, n);
                Use(s.Seed, n * Math.Max(1, s.SeedCost));
                slots -= n;
            }
            return plan;
        }

        /// <summary>
        /// "!밭"처럼 무엇을 심을지 정하지 않았을 때(구상서 6-3 "씨앗이 있고 그 밭의 바이옴에서 자라는 것"): 먹거리 작물을 씨앗 있는 만큼 돌아가며 한 칸씩.
        /// [기본안] 씨앗용 작물(거두면 다른 작물의 씨앗이 나옴 — 당근 씨앗 심기)은 채우지 않고 씨앗 유지(Plan 2단계)로만 심는다.
        /// 씨앗과 수확물이 같은 작물(보리)은 먹을 몫을 남기려 재고의 반까지만. 자리 1/4은 씨앗 유지 몫으로 남긴다.
        /// </summary>
        public static FarmPlan Fill(IList<Crop> crops, int biome, int slots, Func<string, int> stock)
        {
            var food = new List<Crop>();
            if (crops != null)
                foreach (var c in crops)
                {
                    if (c == null || !CanGrow(c, biome) || c.Seed == null || c.Produce == null) continue;
                    bool seedCrop = false;
                    // 당근↔당근 씨앗처럼 서로의 씨앗이면 많이 나오는 쪽(씨앗 3개)이 씨앗용
                    foreach (var d in crops)
                        if (d != null && d != c && d.Seed == c.Produce && (d.Produce != c.Seed || c.Yield > d.Yield)) seedCrop = true;
                    if (!seedCrop && !food.Exists(f => f.Produce == c.Produce)) food.Add(c);
                }
            var left = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var c in food)
                if (!left.ContainsKey(c.Seed))
                {
                    int s = Math.Max(0, stock?.Invoke(c.Seed) ?? 0);
                    left[c.Seed] = c.Seed == c.Produce ? s / 2 : s;
                }
            var count = new int[food.Count];
            int fill = slots - slots / 4;
            for (bool any = true; fill > 0 && any;)
            {
                any = false;
                for (int i = 0; i < food.Count && fill > 0; i++)
                {
                    int cost = Math.Max(1, food[i].SeedCost);
                    if (left[food[i].Seed] < cost) continue;
                    left[food[i].Seed] -= cost;
                    count[i]++;
                    fill--;
                    any = true;
                }
            }
            var want = new List<Ingredient>();
            for (int i = 0; i < food.Count; i++)
                if (count[i] > 0) want.Add(new Ingredient(food[i].Produce, count[i] * Math.Max(1, food[i].Yield)));
            return Plan(crops, biome, slots, stock, want);
        }

        /// <summary>다 자랄 때까지 넉넉히(가장 늦은 작물의 m_growTimeMax) 초. 심을 것이 없으면 0.</summary>
        public static double GrowSeconds(IList<Crop> crops, FarmPlan plan)
        {
            double max = 0;
            if (crops == null || plan == null) return 0;
            foreach (var p in plan.Plant)
                foreach (var c in crops)
                    if (c != null && c.Sapling == p.Item) max = Math.Max(max, Math.Max(c.GrowMin, c.GrowMax));
            return max;
        }
    }
}

namespace ValheimHelper.Core.Work
{
    public sealed class FarmField
    {
        public float X, Z;
        public int Radius;
        public FarmField(float x, float z, int radius) { X = x; Z = z; Radius = radius; }
        public bool Contains(float x, float z) => (X - x) * (X - x) + (Z - z) * (Z - z) <= (float)Radius * Radius;
    }

    public enum FarmSetResult { Added, Moved, Full }

    /// <summary>
    /// "!밭 N"으로 정한 밭들(구상서 6-3, 여러 곳). 두 NPC가 같이 쓰고 NPC ZDO(vh_farms)에 저장한다.
    /// [기본안] 새 밭 중심이 있던 밭 안이면 그 밭을 옮기고(반경도 새 값), 아니면 새로 더한다. 최대 Max곳.
    /// </summary>
    public sealed class FarmBook
    {
        public const int Max = 8;
        readonly List<FarmField> _fields = new List<FarmField>();
        public bool Dirty;
        public IReadOnlyList<FarmField> Fields => _fields;

        public FarmSetResult Set(float x, float z, int radius)
        {
            radius = (int)Farm.ClampRadius(radius);
            foreach (var f in _fields)
                if (f.Contains(x, z))
                {
                    f.X = x; f.Z = z; f.Radius = radius;
                    Dirty = true;
                    return FarmSetResult.Moved;
                }
            if (_fields.Count >= Max) return FarmSetResult.Full;
            _fields.Add(new FarmField(x, z, radius));
            Dirty = true;
            return FarmSetResult.Added;
        }

        /// <summary>(x,z)를 품은 첫 밭(Set이 옮기는 것과 같은 것). 없으면 null.</summary>
        public FarmField At(float x, float z) => _fields.Find(f => f.Contains(x, z));

        /// <summary>(x,z)를 품은 밭 하나를 지운다. 지웠으면 true.</summary>
        public bool Remove(float x, float z)
        {
            int i = _fields.FindIndex(f => f.Contains(x, z));
            if (i < 0) return false;
            _fields.RemoveAt(i);
            Dirty = true;
            return true;
        }

        public string Save()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var parts = new List<string>();
            foreach (var f in _fields) parts.Add(f.X.ToString("0.0", inv) + "," + f.Z.ToString("0.0", inv) + "," + f.Radius.ToString(inv));
            return string.Join(";", parts);
        }

        public static FarmBook Load(string text)
        {
            var b = new FarmBook();
            if (string.IsNullOrEmpty(text)) return b;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var num = System.Globalization.NumberStyles.Float;
            foreach (var part in text.Split(';'))
            {
                var p = part.Split(',');
                if (p.Length != 3 || !float.TryParse(p[0], num, inv, out float x) || !float.TryParse(p[1], num, inv, out float z)
                    || !int.TryParse(p[2], System.Globalization.NumberStyles.Integer, inv, out int r) || r <= 0) continue;
                if (b._fields.Count < Max) b._fields.Add(new FarmField(x, z, (int)Farm.ClampRadius(r)));
            }
            return b;
        }
    }
}
