using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Chat;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>출발 전 판단 입력 모으기(Departures.Input): 장소·적·밤 몫·저항·재고·밤까지 남은 시간.</summary>
    public static partial class Departures
    {
        static DepartureInput Input(WorkerNpc npc, ChatCommand cmd, HashSet<ToolKind> ways, bool fromQueue)
        {
            Vector3 home = npc.Home;
            var input = new DepartureInput
            {
                Personality = npc.Personality,
                Item = GatherJob.ItemLabel(cmd.Item),
                Amount = cmd.Amount,
                Urgent = cmd.Urgent,
                CurrentJob = !fromQueue && npc.Ai.Current == WorkerAI.Mode.Work && npc.Ai.Job != null && !npc.Ai.Yields ? npc.Ai.Job.Label : null,
                Tool = GatherJob.ToolFor(npc, ways, cmd.Item),
                // 지하묘지 진흙 더미에도 Beacon이 붙어 숨는 것으로 잡히지만, 던전 물건은 들어가서 캔다(회차 164: crypttest가 needFinder로 거절됨)
                NeedFinder = !Dungeons.InsideOnly(cmd.Item) && ResourceCatalog.FromHidden(cmd.Item) && !HiddenOres.HaveFinder(),
                Fed = npc.Meals.Count > 0,
                SecondsPerUnit = ways.Contains(ToolKind.Hunt) ? 40f : ways.Count == 1 && ways.Contains(ToolKind.None) ? 1.5f : 4f,
            };
            if (ways.Contains(ToolKind.Axe)) input.Skill = (int)npc.SkillLevels.Level((int)Skills.SkillType.WoodCutting);
            else if (ways.Contains(ToolKind.Pickaxe)) input.Skill = (int)npc.SkillLevels.Level((int)Skills.SkillType.Pickaxes);
            input.FoodInChest = Chests.Any(home, WorkerNpc.HasFood, ChestRole.Food);
            input.HealthMeads = CountMeads(npc.GetInventory()) + Chests.CountAll(home, CountMeads, ChestRole.Food);
            foreach (string k in Resist.Kinds) if (HasResistMead(npc, k)) input.ResistMeads.Add(k);
            input.Stock = Stock(cmd.Item, home);
            GameObject prefab = ObjectDB.instance.GetItemPrefab(cmd.Item);
            int stack = prefab != null ? prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize : 50;
            input.CarryPerTrip = Mathf.Max(1, npc.GetInventory().GetEmptySlots()) * Mathf.Max(1, stack);
            input.SecondsToNight = SecondsToNight();

            // 후보 장소: 아는 곳 가까운 순, 각 곳의 위험을 지금 내 상태(+동료)로 — 장비는 출발 준비에서 챙길 상자의 더 좋은 것까지(⑫ 다)
            float health = npc.GetMaxHealth() + (!input.Fed && input.FoodInChest ? FoodHealthGuess : 0f);
            Prep.Ready(npc, out float armor, out ItemDrop.ItemData weapon, out ItemDrop.ItemData shieldItem);
            armor = Mathf.Max(armor, npc.GetBodyArmor());
            float hit = Mathf.Max(Guard.HitWith(npc, weapon), Guard.HitOf(npc));
            Shield? shield = npc.BlockStats(shieldItem) ?? npc.BlockStats(); // 막기는 그 무리 적 수에 따라(Block.MaxFoes)
            System.Func<IList<Foe>, System.Func<float, float>> taken = f => Guard.TakenFor(npc, armor, shield, f.Count);
            System.Func<GroupFoe, Foe> resolve = Guard.ResolveFor(npc, Prep.Arsenal(npc)); // 적 약점·저항을 내 무기로(전투 보강 나)
            PlaceBook book = Brain.Places;
            double now = Now;
            var biomes = new Dictionary<ZoneXY, int>();
            Knowledge known = Brain.Get();
            System.Func<ZoneXY, int> biomeOf = z => biomes.TryGetValue(z, out int b) ? b : biomes[z] = known.BiomeOf(z) != 0 ? known.BiomeOf(z) : Scout.BiomeAt(Senses.ZoneCenter(z));
            var gearBy = new Dictionary<int, PlaceRisk>();
            int homeBiome = biomeOf(Senses.ZoneOf(home));
            var zones = Brain.Get().ZonesWith(cmd.Item, Senses.ZoneOf(home));
            if (Dungeons.InsideOnly(cmd.Item))
            {
                // 던전 안 물건(검은 핵)은 구역 지식에 안 쌓인다 → 갈 수 있는 던전 입구 구역도 후보로(⑬ 바-5: 시커에게서 도망친 광산에 혼자 다시 들어감)
                foreach (var d in Dungeons.All(cmd.Item))
                {
                    ZoneXY z = Senses.ZoneOf(d.Pos);
                    if (d.Left > 0 && (d.Open || d.CanOpen) && !zones.Exists(x => x.Zone.Equals(z))) zones.Add(new ZoneMemory { Zone = z });
                }
                zones.Sort((a, b) => Utils.DistanceXZ(home, Senses.ZoneCenter(a.Zone)).CompareTo(Utils.DistanceXZ(home, Senses.ZoneCenter(b.Zone))));
            }
            // 밤 몫: 밤이면 그 바이옴 밤 적(장비와 무관)을 더해 잃을 비율을 셈 — 센 장비는 밤 목초지도 괜찮게 나온다(사용자 11-13 17:30)
            bool night = EnvMan.IsNight();
            // extra = 아직 곁에 없는 동료 일꾼 한 방(TeamPlan.Needed가 아는 적의 맞는 인원을 셀 때만 더함)
            System.Func<float, List<NightSpawn>, float, System.Func<IList<Foe>, float>> hitAt = (h, ns, extra) => f =>
            {
                List<Foe> all = NightFoes.Plus(f, ns, resolve);
                return Danger.Ratio(all, h, hit + extra, taken(all));
            };
            System.Func<float, List<NightSpawn>, System.Func<IList<Foe>, float>> ratioAt = (h, ns) => hitAt(h, ns, 0f);
            foreach (ZoneMemory m in zones)
            {
                if (input.Places.Count >= MaxPlaces) break;
                int biome = m.Biome != 0 ? m.Biome : biomeOf(m.Zone);
                List<NightSpawn> nightSpawns = night ? NightOf(biome) : null;
                PlaceRisk alone = book.Risk(m.Zone, now, ratioAt(health, nightSpawns), resolve);
                var o = new PlaceOption
                {
                    Zone = m.Zone,
                    Distance = Utils.DistanceXZ(home, Senses.ZoneCenter(m.Zone)),
                    Ratio = alone.Ratio,
                    RatioWithMead = book.Risk(m.Zone, now, ratioAt(health + MeadHeal, nightSpawns), resolve).Ratio,
                    DeathWeight = alone.DeathWeight,
                    Boss = alone.Boss,
                    TopFoe = alone.TopFoe,
                    Resist = NeedsResist(npc, m.Biome),
                };
                // 거점 바이옴(처음엔 초원)은 장비 검사 안 함: 줄 수 있는 더 좋은 장비가 없어 초반에 일을 못 했다(사용자 결정 2026-10-25)
                if (biome == homeBiome) gearBy[biome] = new PlaceRisk();
                if (!gearBy.TryGetValue(biome, out PlaceRisk land))
                    gearBy[biome] = land = book.BiomeRisk(biome, biomeOf, ratioAt(health, night ? NightOf(biome) : null), resolve, NativeTo); // 밤이면 그 바이옴 밤 몫도(장소 검사와 같은 식)
                o.GearRatio = land.Ratio;
                o.GearFoe = land.TopFoe;
                o.BiomeName = BiomeNames.Of(biome);
                o.InBase = BaseArea.Contains(o.Distance);
                foreach (WorkerNpc w in WorkerNpc.All)
                    if (w != null && w != npc && !w.IsDead() && Senses.ZoneOf(w.transform.position).Equals(m.Zone)) o.Crowd++;
                // 아는 적의 맞는 인원(사용자 11-13 00:15): 호위병 → 다른 일꾼 순으로 한 방씩 더해 셋까지 셈, 셋도 안 되면 0(장비 요청). 모르는 적은 -1(Team 기본)
                if (alone.Known && alone.TopFoe != null)
                {
                    var helpers = new List<float>();
                    WorkerNpc g0 = GuardOf(); // 호위병은 WorkerNpc.Guards에만 있다(All엔 없음 — 11-18 감사)
                    if (g0 != null && g0 != npc && !g0.IsDead()) helpers.Add(Guard.HitOf(g0));
                    foreach (WorkerNpc w in WorkerNpc.All)
                        if (w != null && w != npc && !w.IsDead() && !w.IsGuard) helpers.Add(Guard.HitOf(w));
                    o.Team = Core.Npc.TeamPlan.Needed(extra =>
                    {
                        PlaceRisk pr = book.Risk(m.Zone, now, hitAt(health, nightSpawns, extra), resolve);
                        var probe = new PlaceOption { Ratio = pr.Ratio, Boss = pr.Boss, GearRatio = o.GearRatio }; // 죽은 기록은 맞는 인원이 아니라 덜 위험한 곳 판단에서 본다
                        return !Departure.Risky(npc.Personality, probe, Departure.Level(npc.Personality, probe));
                    }, helpers);
                }
                input.Places.Add(o);
            }
            input.Places.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            WorkerNpc guard = GuardOf();
            input.Guard = guard?.NpcName;
            input.GuardBusy = guard != null && !GuardFree(guard);
            if (cmd.Item == Dungeons.ScrapItem && input.Places.Count == 0) // 거절 대사가 지하묘지 말이라 고철만(서리 동굴은 못 찾으면 일이 찾기·탐험으로)
            {
                input.NoCrypt = !Dungeons.TryChoose(home, out _, out bool minedOut);
                input.CryptsMinedOut = minedOut;
            }
            return input;
        }

        static Dictionary<string, int> s_native;

        /// <summary>
        /// 그 적이 그 바이옴에 나는가(게임 생성 표 ZoneSystem.m_zoneCtrlPrefab의 SpawnSystem.m_spawnLists — 켜진 것만).
        /// 출발 판단의 "이 장비론 평원 무리"가 떠돌다 넘어온 적(초원의 트롤)으로 그 땅 전체를 막지 않게. 표를 못 읽으면 다 셈.
        /// </summary>
        internal static bool NativeTo(string kind, int biome)
        {
            if (s_native == null)
            {
                var map = new Dictionary<string, int>();
                SpawnSystem ss = ZoneSystem.instance != null && ZoneSystem.instance.m_zoneCtrlPrefab != null ? ZoneSystem.instance.m_zoneCtrlPrefab.GetComponentInChildren<SpawnSystem>(true) : null;
                if (ss != null)
                    foreach (SpawnSystemList list in ss.m_spawnLists)
                        foreach (SpawnSystem.SpawnData d in list.m_spawners)
                            if (d.m_enabled && d.m_prefab != null)
                                map[d.m_prefab.name] = (map.TryGetValue(d.m_prefab.name, out int m) ? m : 0) | (int)d.m_biome;
                if (map.Count == 0) return true; // 아직 못 읽음 — 다음에 다시
                s_native = map;
                Plugin.Log.LogInfo($"생성 표: 적 {map.Count}종 바이옴 읽음");
            }
            return s_native.TryGetValue(kind, out int mask) && (mask & biome) != 0;
        }

        static List<SpawnSystem.SpawnData> s_nightRows;

        /// <summary>
        /// 밤에 새로 시키는 탐험 판단(사용자 11-13 — 밤이라고 무조건 거절하지 않고 위험으로 본다): 그 바이옴을 지금 장비로 상대할 때 밤 몫을 더한 비율이 HardFrom 미만이면 간다.
        /// 장비가 센 일꾼은 밤 목초지도 괜찮게 나온다. 장비와 상관없는 고정 공포 값은 넣지 않는다.
        /// </summary>
        internal static bool NightGearOk(WorkerNpc npc, int biome) => NightRatio(npc, biome) < Departure.HardFrom;

        /// <summary>NightGearOk의 값: 그 바이옴 밤 몫을 더한 장비 위험 비율(시험이 숫자로 남김).</summary>
        internal static float NightRatio(WorkerNpc npc, int biome) => NightRatio(npc, biome, out _, out _);
        internal static float NightRatio(WorkerNpc npc, int biome, out float landPart, out float nightPart)
        {
            Prep.Ready(npc, out float armor, out ItemDrop.ItemData weapon, out ItemDrop.ItemData shieldItem);
            armor = Mathf.Max(armor, npc.GetBodyArmor());
            float health = npc.GetMaxHealth();
            float hit = Mathf.Max(Guard.HitWith(npc, weapon), Guard.HitOf(npc));
            Shield? shield = npc.BlockStats(shieldItem) ?? npc.BlockStats();
            System.Func<IList<Foe>, System.Func<float, float>> taken = f => Guard.TakenFor(npc, armor, shield, f.Count);
            System.Func<GroupFoe, Foe> resolve = Guard.ResolveFor(npc, Prep.Arsenal(npc));
            List<NightSpawn> nightSpawns = NightOf(biome);
            Knowledge known = Brain.Get();
            System.Func<ZoneXY, int> biomeOf = z => known.BiomeOf(z) != 0 ? known.BiomeOf(z) : Scout.BiomeAt(Senses.ZoneCenter(z));
            // 밤 적은 하나씩 생기므로 한꺼번에 다 붙지 않는다고 셈(사용자 11-14): 낮 기억 적은 다 셈, 밤 적은 가장 센 둘만 겹침
            List<Foe> nightOnly = NightFoes.Plus(new List<Foe>(), nightSpawns, resolve);
            PlaceRisk land = Brain.Places.BiomeRisk(biome, biomeOf, f =>
                Danger.NightRatio(f, nightOnly, health, hit, taken), resolve, NativeTo);
            // 기억한 자리가 없어도 밤에만 나는 적 몫은 센다(새 월드에서 밤 늪이 0으로 나오던 것 — 회차 563 vh_nightexploretest)
            float alone = nightOnly.Count > 0 ? Danger.NightRatio(new List<Foe>(), nightOnly, health, hit, taken) : 0f;
            landPart = land.Ratio;
            nightPart = alone;
            return Mathf.Max(land.Ratio, alone);
        }

        /// <summary>시험 DIAG: 밤 적 하나하나의 체력·한 방·받는 피해·혼자일 때 비율과 일꾼 체력·한 방.</summary>
        internal static string NightDetail(WorkerNpc npc, int biome)
        {
            Prep.Ready(npc, out float armor, out ItemDrop.ItemData weapon, out ItemDrop.ItemData shieldItem);
            armor = Mathf.Max(armor, npc.GetBodyArmor());
            float health = npc.GetMaxHealth(), hit = Mathf.Max(Guard.HitWith(npc, weapon), Guard.HitOf(npc));
            Shield? shield = npc.BlockStats(shieldItem) ?? npc.BlockStats();
            System.Func<GroupFoe, Foe> resolve = Guard.ResolveFor(npc, Prep.Arsenal(npc));
            var parts = new List<string> { $"일꾼 체력 {health:0}·한 방 {hit:0}·방어 {armor:0}" };
            foreach (Foe f in NightFoes.Plus(new List<Foe>(), NightOf(biome), resolve))
            {
                var one = new List<Foe> { f };
                System.Func<float, float> taken = Guard.TakenFor(npc, armor, shield, 1);
                parts.Add($"{f.Kind} 체력 {f.Health:0}·한 방 {f.Damage:0}→{taken(f.Damage):0}·혼자 {Danger.Ratio(one, health, hit, taken):0.00}");
            }
            return string.Join(" / ", parts.ToArray());
        }

        /// <summary>
        /// 그 바이옴 밤 몫(게임 생성 표의 밤에만 나는 줄: 켜짐·m_spawnAtDay 꺼짐·m_spawnAtNight 켜짐, 환경 조건 없음, 전역 열쇠가 있으면 켜졌을 때만).
        /// 수는 m_maxSpawned. 표를 못 읽으면 빈 목록(밤 몫 없음 — 지금처럼).
        /// </summary>
        internal static List<NightSpawn> NightOf(int biome)
        {
            if (s_nightRows == null)
            {
                SpawnSystem ss = ZoneSystem.instance != null && ZoneSystem.instance.m_zoneCtrlPrefab != null ? ZoneSystem.instance.m_zoneCtrlPrefab.GetComponentInChildren<SpawnSystem>(true) : null;
                if (ss == null) return new List<NightSpawn>();
                var rows = new List<SpawnSystem.SpawnData>();
                foreach (SpawnSystemList list in ss.m_spawnLists)
                    foreach (SpawnSystem.SpawnData d in list.m_spawners)
                        if (d.m_enabled && d.m_prefab != null && !d.m_spawnAtDay && d.m_spawnAtNight && d.m_maxSpawned > 0 && d.m_requiredEnvironments.Count == 0 && Hostile(d.m_prefab))
                            rows.Add(d);
                s_nightRows = rows;
                Plugin.Log.LogInfo($"생성 표: 밤에만 나는 줄 {rows.Count}개 읽음");
            }
            var night = new List<NightSpawn>();
            foreach (SpawnSystem.SpawnData d in s_nightRows)
                if (((int)d.m_biome & biome) != 0 && (string.IsNullOrEmpty(d.m_requiredGlobalKey) || ZoneSystem.instance.GetGlobalKey(d.m_requiredGlobalKey)))
                    night.Add(new NightSpawn { Kind = d.m_prefab.name, Count = d.m_maxSpawned });
            return night;
        }

        /// <summary>싸움을 거는 생물만(밤 늪 줄의 odin처럼 Character 없는 것·짐승·플레이어 편은 뺌 — Guard 적 고르기와 같은 기준)</summary>
        static bool Hostile(GameObject prefab)
        {
            Character c = prefab.GetComponent<Character>();
            return c != null && c.m_faction != Character.Faction.Players && c.m_faction != Character.Faction.AnimalsVeg && !(prefab.GetComponent<BaseAI>() is AnimalAI);
        }

        static Dictionary<string, string> Args(WorkerNpc npc, DepartureResult r)
        {
            var args = new Dictionary<string, string>(r.Args);
            if (r.Args.TryGetValue("foe", out string kind)) args["foe"] = FoeName(kind);
            if (r.Alternative != null)
            {
                Scout.FromHome(npc.Home, Senses.ZoneCenter(r.Alternative.Zone), out string dir, out string dist);
                args["altDir"] = dir;
                args["altDist"] = dist;
            }
            if (!args.ContainsKey("partner")) args["partner"] = GuardOf()?.NpcName ?? "";
            if (!args.ContainsKey("extra")) args["extra"] = "";
            return args;
        }

        public static string FoeName(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return "";
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(kind) : null;
            Character c = prefab != null ? prefab.GetComponent<Character>() : null;
            return c != null ? Localization.instance.Localize(c.m_name) : kind;
        }

        /// <summary>그 저항 미드를 짐이나 음식·미드 상자에 가졌는지(Prep이 거기서 챙김).</summary>
        static bool HasResistMead(WorkerNpc npc, string kind) =>
            npc.HasResistMead(kind) || Chests.Any(npc.Home, inv => inv.GetAllItems().Exists(it => Gear.ResistOf(it) == kind), ChestRole.Food);

        /// <summary>그 구역 바이옴(기억, 게임 Heightmap.Biome 비트)에서 막아야 할 피해. 모르거나 장비로 이미 막으면 null.</summary>
        static string NeedsResist(WorkerNpc npc, int biome)
        {
            for (int bit = 1; bit <= biome && bit > 0; bit <<= 1)
            {
                if ((biome & bit) == 0) continue;
                string k = Resist.ForBiome(((Heightmap.Biome)bit).ToString());
                if (k != null && !npc.Resists(k)) return k;
            }
            return null;
        }

        static int CountMeads(Inventory inv)
        {
            int n = 0;
            foreach (var it in inv.GetAllItems()) if (Gear.IsHealthMead(it)) n += it.m_stack;
            return n;
        }

        static int Stock(string prefab, Vector3 home)
        {
            GameObject go = ObjectDB.instance.GetItemPrefab(prefab);
            if (go == null) return 0;
            string name = go.GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
            int n = 0;
            foreach (var c in Chests.NearBase(home, ChestRole.Mats)) n += c.GetInventory().CountItems(name);
            return n;
        }

        /// <summary>밤까지 남은 초(바닐라 EnvMan: 하루의 0.75부터 0.25까지 밤). 이미 밤이면 0.</summary>
        static float SecondsToNight()
        {
            EnvMan env = EnvMan.instance;
            if (env == null) return float.PositiveInfinity;
            float f = DayFraction(env);
            if (f <= 0.25f || f >= 0.75f) return 0f;
            return Mathf.Max(0f, (0.75f - f) * env.m_dayLengthSec);
        }

        /// <summary>
        /// 지금 하루 비율(바닐라 EnvMan.RescaleDayFraction과 같은 꼴). GetDayFraction은 매 틱 1%씩 따라가는 부드러운 값이라 잠자기 등으로
        /// 시각이 뛰면 몇 초 동안 옛 저녁 값을 준다(회차 90 departtest: 아침에 꺼낸 일을 "다녀오면 깜깜" 하고 다시 물음) → 서버 시각에서 바로 센다.
        /// 시험의 시각 고정(m_debugTimeOfDay)은 그 값(m_debugTime)을 바로 쓴다.
        /// </summary>
        static float DayFraction(EnvMan env)
        {
            if (env.m_debugTimeOfDay) return env.m_debugTime; // GetDayFraction은 다음 FixedUpdate에야 바뀐다(프레임이 빠르면 한 프레임 늦음)
            if (ZNet.instance == null || env.m_dayLengthSec <= 0) return env.GetDayFraction();
            double len = env.m_dayLengthSec;
            float raw = (float)(ZNet.instance.GetTimeSeconds() % len / len);
            if (raw >= 0.15f && raw <= 0.85f) return 0.25f + (raw - 0.15f) / 0.7f * 0.5f;
            return raw < 0.5f ? raw / 0.15f * 0.25f : 0.75f + (raw - 0.85f) / 0.15f * 0.25f;
        }
    }
}
