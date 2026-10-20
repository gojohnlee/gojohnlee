using System;

namespace ValheimHelper.Core.Work
{
    /// <summary>
    /// 새 땅 탐험 (구상서 8-2장): 지도에서 밝혀진 곳과 안 밝혀진 곳의 경계 구역을 고른다.
    /// 거점에서 가까운 경계부터 넓혀 가되, 지금 있는 곳에서도 가까운 쪽을 골라 왔다 갔다 하지 않는다.
    /// </summary>
    public static class ExplorePlan
    {
        /// <param name="explored">지도에서 밝혀졌는지.</param>
        /// <param name="land">걸어서 갈 수 있는 땅인지(바다는 못 건넌다).</param>
        /// <param name="dirX">!탐험 동쪽처럼 방향을 줬으면 그 방향(구역 좌표, 북 = +Y). 없으면 0, 0.</param>
        /// <param name="prefer">먼저 갈 경계: 이웃한 밝혀진 구역이 이 조건이면(그 바이옴 쪽). 없으면 null.</param>
        /// <returns>다음 경계 구역. 더 없으면 false. 가 봤거나 못 가는 구역(지식의 "둘러봄")은 건너뛴다.</returns>
        public static bool Next(ZoneXY home, ZoneXY from, int maxRing, Knowledge k,
            Func<ZoneXY, bool> explored, Func<ZoneXY, bool> land, int dirX, int dirY, Func<ZoneXY, bool> prefer, out ZoneXY next)
        {
            next = default;
            bool found = false, bestPreferred = false;
            double bestScore = double.MaxValue;
            double dirLen = Math.Sqrt(dirX * dirX + dirY * dirY);
            for (int dy = -maxRing; dy <= maxRing; dy++)
                for (int dx = -maxRing; dx <= maxRing; dx++)
                {
                    if (dirLen > 0)
                    {
                        if (dx == 0 && dy == 0) continue;
                        // 그 방향 45° 안쪽만
                        if (dx * dirX + dy * dirY < 0.7 * dirLen * Math.Sqrt(dx * dx + dy * dy)) continue;
                    }
                    var z = new ZoneXY(home.X + dx, home.Y + dy);
                    if (k.IsLooked(z) || explored(z) || !land(z)) continue;
                    bool frontier = false, preferred = false;
                    for (int ny = -1; ny <= 1; ny++)
                        for (int nx = -1; nx <= 1; nx++)
                        {
                            if (nx == 0 && ny == 0) continue;
                            var n = new ZoneXY(z.X + nx, z.Y + ny);
                            if (!explored(n)) continue;
                            frontier = true;
                            if (prefer != null && prefer(n)) preferred = true;
                        }
                    if (!frontier) continue;
                    // 방향이 없으면 거점 가까운 경계부터 넓힌다. 방향이 있으면 그쪽으로 더 나아간 경계(지금 있는 곳 근처)를 고른다:
                    // 거점 거리를 쓰면 띠 모양으로 밝혀진 땅의 양옆을 왔다 갔다 했다(0.3.6 시험).
                    double score = dirLen > 0
                        ? Math.Sqrt(z.DistSq(from)) - (dx * dirX + dy * dirY) / dirLen
                        : Math.Sqrt(z.DistSq(home)) + Math.Sqrt(z.DistSq(from));
                    if (bestPreferred && !preferred) continue;
                    if ((preferred && !bestPreferred) || score < bestScore)
                    {
                        bestScore = score;
                        bestPreferred = preferred;
                        next = z;
                        found = true;
                    }
                }
            return found;
        }
    }

    /// <summary>방위: 채팅의 방향 이름 ↔ 구역 방향, 거리 말하기용 8방위 (북 = +Y).</summary>
    public static class Bearing
    {
        static readonly string[] Names = { "east", "northeast", "north", "northwest", "west", "southwest", "south", "southeast" };
        static readonly string[] Korean = { "동쪽", "북동쪽", "북쪽", "북서쪽", "서쪽", "남서쪽", "남쪽", "남동쪽" };
        static readonly int[] Dx = { 1, 1, 0, -1, -1, -1, 0, 1 };
        static readonly int[] Dy = { 0, 1, 1, 1, 0, -1, -1, -1 };

        /// <summary>"east" 같은 이름(Places.Direction 결과) → 방향. 모르면 false.</summary>
        public static bool Vector(string name, out int dx, out int dy)
        {
            int i = Array.IndexOf(Names, name);
            dx = i >= 0 ? Dx[i] : 0;
            dy = i >= 0 ? Dy[i] : 0;
            return i >= 0;
        }

        /// <summary>(dx, dy) 쪽의 한국어 8방위. 0이면 null.</summary>
        public static string Of(double dx, double dy)
        {
            if (dx == 0 && dy == 0) return null;
            double deg = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            int i = (int)Math.Round(deg / 45.0);
            return Korean[((i % 8) + 8) % 8];
        }

        public static string KoreanOf(string name)
        {
            int i = Array.IndexOf(Names, name);
            return i >= 0 ? Korean[i] : name;
        }
    }

    /// <summary>바이옴 한국어 이름 (값은 게임 Heightmap.Biome 비트).</summary>
    public static class BiomeNames
    {
        static readonly int[] Bits = { 1, 2, 4, 8, 16, 32, 64, 256, 512 };
        static readonly string[] Korean = { "초원", "늪", "산", "검은 숲", "평원", "애쉬랜드", "딥노스", "바다", "안개땅" };
        static readonly string[] Game = { "Meadows", "Swamp", "Mountain", "BlackForest", "Plains", "AshLands", "DeepNorth", "Ocean", "Mistlands" };

        public static int FromGame(string name)
        {
            int i = Array.IndexOf(Game, name);
            return i >= 0 ? Bits[i] : 0;
        }

        /// <summary>비트 합 → "검은 숲·늪". 없으면 빈 문자열.</summary>
        public static string Of(int mask)
        {
            var parts = new System.Collections.Generic.List<string>();
            for (int i = 0; i < Bits.Length; i++) if ((mask & Bits[i]) != 0) parts.Add(Korean[i]);
            return string.Join("·", parts);
        }
    }
}
