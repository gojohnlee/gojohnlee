using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// vh_ashlandstest (⑭ 애쉬랜드 1조각): 애쉬랜드 물건(불꽃금속 광석·그라우스텐·검은나무·탄 뼈 등)을 게임 데이터에서 어떻게 얻는지
    /// (캐기·줍기·사냥·밭)와 애쉬랜드 적(탄 자·모르겐·발키리·아스크스빈·볼처 등) 한 마리 위험 비율을 시험 일꾼 장비로 diag에 남긴다.
    /// 다음 조각(자리·용암 피하기·불 저항·광석 캐기·싸움)의 바탕 자료 — 판정은 프리팹이 있는지·데이터로 셀 수 있는지만, 나머지는 기록.
    /// </summary>
    static class AshlandsTest
    {
        public static void Register() => TestRunner.Register("vh_ashlandstest", "애쉬랜드 자료 시험", () => Run(), 1200f);

        static IEnumerator Run()
        {
            var checks = new GatherTest.Checks();
            if (Player.m_localPlayer == null || !ZNet.instance.IsServer())
            {
                TestRunner.Log("FAIL: 월드에 들어간 뒤(싱글 또는 서버 시작) 실행해야 합니다.");
                TestRunner.Finish();
                yield break;
            }
            var w = new TestKit.World();
            try { yield return Body(w, checks); }
            finally { TestKit.Restore(w); }
            TestKit.End(w, checks);
        }

        static readonly string[] Items = { "FlametalOreNew", "FlametalNew", "Grausten", "Blackwood", "CharredBone", "MorgenSinew", "MorgenHeart",
            "AskHide", "AskBladder", "AsksvinMeat", "VoltureMeat", "VoltureEgg", "CelestialFeather", "CharcoalResin", "ProustitePowder",
            "SulfurStone", "MoltenCore", "Vineberry", "Fiddleheadfern", "BonemawSerpentTooth", "BonemawSerpentMeat" };
        static readonly string[] Foes = { "Charred_Melee", "Charred_Archer", "Charred_Twitcher", "Charred_Mage", "Morgen", "FallenValkyrie",
            "Asksvin", "Volture", "BlobLava", "BonemawSerpent" };

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC", w.Ragnar != null);
            if (w.Ragnar == null) yield break;

            foreach (string item in Items)
            {
                if (ObjectDB.instance.GetItemPrefab(item) == null) { TestRunner.Log($"{item}: 물건 프리팹 없음"); continue; }
                var ways = ResourceCatalog.WaysFor(item);
                Crop crop = Crops.ForProduce(item);
                TestRunner.Log($"{item}: 방법 [{string.Join(",", new List<ToolKind>(ways).ConvertAll(t => t.ToString()).ToArray())}], 사냥감 {Prey.Any(item)}, " +
                    $"출처 [{string.Join(", ", ResourceCatalog.SourcesOf(item).ToArray())}], 작물 {(crop != null ? crop.Sapling + " 바이옴 " + crop.Biomes : "없음")}");
            }
            checks.Add("불꽃금속 광석 프리팹 있음", ObjectDB.instance.GetItemPrefab("FlametalOreNew") != null);
            checks.Add("불꽃금속 광석을 얻는 길이 있음", ResourceCatalog.WaysFor("FlametalOreNew").Count > 0);
            checks.Add("그라우스텐을 얻는 길이 있음", ResourceCatalog.WaysFor("Grausten").Count > 0);

            float hp = w.Ragnar.GetMaxHealth(), hit = Guard.HitOf(w.Ragnar);
            var taken = Guard.TakenFor(w.Ragnar);
            TestRunner.Log($"시험 일꾼: 체력 {hp:0}, 한 방 {hit:0.0}, 갑옷 {w.Ragnar.GetBodyArmor():0}");
            foreach (string kind in Foes)
            {
                GameObject p = ZNetScene.instance.GetPrefab(kind);
                Character c = p != null ? p.GetComponent<Character>() : null;
                if (c == null) { TestRunner.Log($"{kind}: 프리팹 없음"); continue; }
                Foe f = Guard.Resolve(new GroupFoe { Kind = kind, Count = 1, Level = 1 });
                float ratio = Danger.Ratio(new List<Foe> { f }, hp, hit, taken);
                float data = Guard.DataHit(c);
                TestRunner.Log($"{kind}: 체력 {f.Health:0}, 한 방 {f.Damage:0.0}(데이터 {data:0.0}), 날기 {c.m_flying}, 무리 {c.m_group}, " +
                    $"토박이 {Departures.NativeTo(kind, (int)Heightmap.Biome.AshLands)}, 위험 비율 {ratio:0.00}");
                var el = c is Humanoid ? Guard.FoeElementDamage(c) : null;
                if (el != null && el.Count > 0) TestRunner.Log($"{kind}: 속성 피해 {string.Join(", ", el.Select(e => e.Key + " " + e.Value.ToString("0")))}");
                if (kind == "Charred_Melee" || kind == "Morgen" || kind == "Asksvin")
                    checks.Add($"{kind} 한 방을 게임 데이터로 셈 (데이터 {data:0.0})", data > 0f);
            }
            checks.Add("애쉬랜드 적 프리팹(탄 전사·모르겐) 있음",
                ZNetScene.instance.GetPrefab("Charred_Melee") != null && ZNetScene.instance.GetPrefab("Morgen") != null);
            checks.Add("애쉬랜드 토박이 적에 탄 전사가 있음", Departures.NativeTo("Charred_Melee", (int)Heightmap.Biome.AshLands));
            // 바) 불 저항: 애쉬랜드 출발 판단이 막을 피해(Resist.ForBiome)와 그걸 막는 물건(Gear.ResistOf)
            checks.Add("애쉬랜드에서 막을 피해 = 불", Resist.ForBiome(Heightmap.Biome.AshLands.ToString()) == "Fire");
            var fire = ObjectDB.instance.m_items.Where(g => g != null && g.GetComponent<ItemDrop>() != null
                && Gear.ResistOf(g.GetComponent<ItemDrop>().m_itemData) == "Fire").Select(g => g.name).ToList();
            checks.Add($"불 저항 마실 것이 있음 ({string.Join(", ", fire)})", fire.Count > 0);
            if (TestKit.RunBiome != Heightmap.Biome.AshLands) yield break;
            Site(w, checks);
            yield return Gather(w, checks);
            yield return Pickups(w, checks);
            yield return LavaWalk(w, checks);
            yield return Flametal(w, checks);
            yield return FarSulfur(w, checks);
        }

        static readonly string[] GatherGear = { "ArmorPaddedCuirass", "ArmorPaddedGreaves", "HelmetPadded", "CapeLinen", "SwordSilver" };

        /// <summary>도구 종류·단계에 맞는 가장 낮은 도구 프리팹(없으면 null).</summary>
        static string ToolFor(ToolKind kind, int tier)
        {
            string[] names = kind == ToolKind.Axe
                ? new[] { "AxeStone", "AxeFlint", "AxeBronze", "AxeIron", "AxeBlackMetal", "AxeJotunBane" }
                : new[] { "PickaxeAntler", "PickaxeBronze", "PickaxeIron", "PickaxeBlackMetal" };
            foreach (string n in names)
                if ((ObjectDB.instance.GetItemPrefab(n)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_toolTier ?? -1) >= tier) return n;
            return null;
        }

        /// <summary>
        /// ⑭ 나) 애쉬랜드 자리 채집: 단계 맞는 곡괭이·도끼를 주고 "!Grausten 5"·"!Blackwood 5"를 지금 GatherJob 그대로 → 거점 상자에.
        /// 탄 해골 생성기(Spawner_CharredStone) 둘레 적은 시험 동안 거점·가장 가까운 자원 둘레에서 치운다(싸움은 바) 조각).
        /// </summary>
        static IEnumerator Gather(TestKit.World w, GatherTest.Checks checks)
        {
            if (w.Floki == null || w.Chest == null) yield break;
            var tools = new List<string>();
            var near = new List<Vector3>();
            foreach (string item in new[] { "Grausten", "Blackwood" })
            {
                int tier = 0;
                ToolKind kind = ToolKind.None;
                ZDO best = null;
                float bd = float.MaxValue;
                foreach (var z in Senses.Nearby(w.Home, 150f, z => ResourceCatalog.Get(z.GetPrefab())?.Produces.Contains(item) == true))
                {
                    ResourceInfo info = ResourceCatalog.Get(z.GetPrefab());
                    if (info.Tool == ToolKind.Hunt || info.Tool == ToolKind.None) continue;
                    kind = info.Tool;
                    tier = Mathf.Max(tier, info.MinToolTier);
                    float d = Utils.DistanceXZ(w.Home, z.GetPosition());
                    if (d < bd) { bd = d; best = z; }
                }
                string tool = kind == ToolKind.None ? null : ToolFor(kind, tier);
                TestRunner.Log($"{item}: {kind} 단계 {tier} → {tool ?? "없음"}, 가장 가까운 {(best == null ? "-" : bd.ToString("0") + "m")}");
                checks.Add($"{item}에 맞는 도구 있음 ({tool ?? "없음"})", tool != null);
                if (tool == null) yield break;
                tools.Add(tool);
                near.Add(best.GetPosition());
            }
            foreach (var npc in new[] { w.Ragnar, w.Floki })
            {
                foreach (var n in GatherGear) npc.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab(n), 1);
                foreach (var t in tools) npc.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab(t), 1);
                Prep.WearBest(npc);
                var food = new Inventory("vh_test_food", null, 4, 2);
                foreach (var n in new[] { "CookedLoxMeat", "Sausages", "SerpentStew" }) food.AddItem(ObjectDB.instance.GetItemPrefab(n), 1);
                npc.ResetMeals();
                npc.EatFrom(food);
                npc.Heal(1000f, false);
            }
            s_keep = true;
            Plugin.Instance.StartCoroutine(TestKit.KeepSiteClear(w.Home, () => s_keep));
            foreach (var p in near) Plugin.Instance.StartCoroutine(TestKit.KeepSiteClear(p, () => s_keep));
            try
            {
                TestKit.ClearBaseStock(w, "Grausten", "Blackwood");
                int deaths0 = w.Ragnar.Deaths + w.Floki.Deaths;
                GatherTest.Say("!Grausten 5", null);
                yield return TestKit.WaitFor(() => GatherTest.CountBase(w.Home, "Grausten") >= 5, 420f);
                checks.Add($"그라우스텐 5개가 상자에 ({GatherTest.CountBase(w.Home, "Grausten")}개, {TestKit.Waited:0}초)", TestKit.Ok);
                yield return TestKit.WaitFor(() => !(w.Ragnar.Ai.Job is GatherJob) && !(w.Floki.Ai.Job is GatherJob), 60f);
                GatherTest.Say("!Blackwood 5", null);
                yield return TestKit.WaitFor(() => GatherTest.CountBase(w.Home, "Blackwood") >= 5, 420f);
                checks.Add($"검은나무 5개가 상자에 ({GatherTest.CountBase(w.Home, "Blackwood")}개, {TestKit.Waited:0}초)", TestKit.Ok);
                checks.Add("안 죽음", w.Ragnar.Deaths + w.Floki.Deaths == deaths0);
                TestRunner.Log($"자원 둘레 치운 생물 {TestKit.KeptClear}");
                TestKit.ClearBaseStock(w, "Grausten", "Blackwood");
            }
            finally { s_keep = false; }
        }

        static readonly string[] PickItems = { "SulfurStone", "MoltenCore", "VoltureEgg", "Vineberry", "Fiddleheadfern" };

        /// <summary>
        /// 마) 맨손 줍기: 유황·용융 핵·볼처 알·바인베리·고사리를 지금 GatherJob 그대로 "!이름 n"(150m 안 출처 수까지, 최대 2) → 거점 상자에.
        /// 150m 안 출처가 없으면 300m 안 가장 가까운 출처 거리·용암 위인지만 diag에. 용암 위 시간·죽음도 본다.
        /// </summary>
        static IEnumerator Pickups(TestKit.World w, GatherTest.Checks checks)
        {
            if (w.Floki == null || w.Chest == null) yield break;
            s_keep = true;
            Plugin.Instance.StartCoroutine(TestKit.KeepSiteClear(w.Home, () => s_keep));
            try
            {
                foreach (string item in PickItems)
                {
                    var src = Senses.Nearby(w.Home, 300f, z => ResourceCatalog.Get(z.GetPrefab()) is ResourceInfo i && i.Tool == ToolKind.None && i.Produces.Contains(item));
                    ZDO best = null;
                    float bd = float.MaxValue;
                    int near = 0, inside = 0;
                    foreach (var z in src)
                    {
                        float d = Utils.DistanceXZ(w.Home, z.GetPosition());
                        if (d <= 150f && !Character.InInterior(z.GetPosition())) near++;
                        else if (d <= 150f) inside++;
                        if (d < bd) { bd = d; best = z; }
                    }
                    TestRunner.Log($"{item}: 300m 안 출처 {src.Count}, 150m 안 바깥 {near}·던전 안 {inside}, 가장 가까운 " +
                        (best == null ? "-" : $"{ZNetScene.instance.GetPrefab(best.GetPrefab())?.name} {bd:0}m 용암 {WorkerAI.Lava(best.GetPosition())}"));
                    foreach (var z in src)
                        if (Character.InInterior(z.GetPosition()) && Utils.DistanceXZ(w.Home, z.GetPosition()) <= 150f)
                        {
                            Vector3 p = z.GetPosition();
                            float g = ZoneSystem.instance.GetGroundHeight(p);
                            TestRunner.Log($"  {item} 출처 거점에서 ({p.x - w.Home.x:0},{p.z - w.Home.z:0}) 구역 {Senses.ZoneOf(p)} 높이 {p.y:0}(땅 {g:0}) 땀 {z.GetBool(ZDOVars.s_picked)}");
                            ZDO loc = null;
                            float ld = float.MaxValue;
                            foreach (var lz in ZdoScan.All(Dungeons.ProxyPrefab))
                            {
                                float d = Utils.DistanceXZ(lz.GetPosition(), p);
                                if (d < ld) { ld = d; loc = lz; }
                            }
                            string ln = "-";
                            if (loc != null)
                                foreach (var l in ZoneSystem.instance.m_locations)
                                    if (!string.IsNullOrEmpty(l.m_prefabName) && l.m_prefabName.GetStableHashCode() == loc.GetInt(ZDOVars.s_location)) ln = l.m_prefabName;
                            TestRunner.Log($"  가장 가까운 장소 {ln} {ld:0}m");
                        }
                    if (near == 0) continue;
                    Plugin.Instance.StartCoroutine(TestKit.KeepSiteClear(best.GetPosition(), () => s_keep));
                    int n = Mathf.Min(2, near);
                    TestKit.ClearBaseStock(w, item);
                    int deaths0 = w.Ragnar.Deaths + w.Floki.Deaths;
                    float lava0 = w.Ragnar.Ai.LavaTime + w.Floki.Ai.LavaTime;
                    GatherTest.Say($"!{item} {n}", null);
                    yield return TestKit.WaitFor(() => GatherTest.CountBase(w.Home, item) >= n, 300f);
                    checks.Add($"{item} {n}개가 상자에 ({GatherTest.CountBase(w.Home, item)}개, {TestKit.Waited:0}초)", TestKit.Ok);
                    if (!TestKit.Ok)
                        foreach (var npc in new[] { w.Ragnar, w.Floki })
                        {
                            npc.Ai.Queue.Clear();
                            Departures.Forget(npc);
                            npc.Ai.GoHome();
                            npc.TeleportTo(w.Home + new Vector3(npc == w.Ragnar ? 2f : -2f, 0.5f, 2f), npc.transform.rotation, false);
                        }
                    float lava = w.Ragnar.Ai.LavaTime + w.Floki.Ai.LavaTime - lava0;
                    checks.Add($"{item}: 용암 위 {lava:0.0}초·안 죽음", lava < 1f && w.Ragnar.Deaths + w.Floki.Deaths == deaths0);
                    yield return TestKit.WaitFor(() => !(w.Ragnar.Ai.Job is GatherJob) && !(w.Floki.Ai.Job is GatherJob), 60f);
                    TestKit.ClearBaseStock(w, item);
                }
            }
            finally { s_keep = false; }
        }


        /// <summary>WorldGenerator 땅 마스크로 본 용암(지형이 안 불러진 먼 곳도 셈 — 불러진 Heightmap의 칠 마스크는 이 마스크에서 시작, HeightmapBuilder).</summary>
        static bool GenLava(Vector3 p, float edge = 0.6f)
        {
            if (WorldGenerator.instance.GetBiome(p) != Heightmap.Biome.AshLands) return false;
            WorldGenerator.instance.GetHeight(p.x, p.z, out Color m);
            return m.a > edge;
        }

        /// <summary>
        /// 다) 용암 피하기: 거점에서 가장 가까운 용암 웅덩이(6m 둘레도 용암) 양쪽의 마른 자리 A·B(곧장 가면 용암을 지남)를 찾아,
        /// A에 선 일꾼이 B의 플레이어를 따라올 때 용암을 밟지 않고 닿는지.
        /// </summary>
        static IEnumerator LavaWalk(TestKit.World w, GatherTest.Checks checks)
        {
            if (w.Floki == null) yield break;
            Vector3? pool = null;
            for (float r = 60f; r <= 900f && pool == null; r += 15f)
                for (int k = 0; k < 48 && pool == null; k++)
                {
                    float a = k * Mathf.PI / 24f;
                    Vector3 p = w.Home + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                    bool ok = GenLava(p);
                    for (int j = 0; j < 8 && ok; j++) ok = GenLava(p + new Vector3(Mathf.Cos(j * Mathf.PI / 4f), 0f, Mathf.Sin(j * Mathf.PI / 4f)) * 6f);
                    if (ok && Spots(p, out _, out _)) pool = p;
                }
            checks.Add($"용암 웅덩이와 양쪽 마른 자리 찾음 ({(pool == null ? "-" : Utils.DistanceXZ(pool.Value, w.Home).ToString("0") + "m")})", pool != null);
            if (pool == null) yield break;
            var pairs = new List<Vector3[]>();
            Spots(pool.Value, out _, out _, pairs);
            Vector3 a0 = pairs[0][0], b0 = pairs[0][1];
            Player player = Player.m_localPlayer;
            s_keep = true;
            Plugin.Instance.StartCoroutine(TestKit.KeepSiteClear(pool.Value, () => s_keep));
            try
            {
                yield return Tp(player, a0 + Vector3.up * 1f, player.transform.rotation, true);
                // 부서진 건물(Ashlands_Ruins 벽·바닥) 안 자리는 뺀다 — 담 미로는 용암 피하기와 따로(회차 223: 담 안에서 시작하면 못 빠져나옴)
                int mask = LayerMask.GetMask("piece", "piece_nonsolid");
                Vector3[] clear = pairs.Find(ab => Physics.OverlapSphere(ab[0], 10f, mask).Length == 0 && Physics.OverlapSphere(ab[1], 10f, mask).Length == 0);
                checks.Add($"건물 없는 양쪽 자리 ({pairs.Count}쌍 중)", clear != null);
                if (clear == null) yield break;
                if (Utils.DistanceXZ(clear[0], a0) > 3f) yield return Tp(player, clear[0] + Vector3.up * 1f, player.transform.rotation);
                a0 = clear[0]; b0 = clear[1];
                foreach (var p in new[] { a0, b0 }) Plugin.Instance.StartCoroutine(TestKit.KeepSiteClear(p, () => s_keep));
                ZoneSystem.instance.GetGroundHeight(a0, out float ha);
                Heightmap hm = Heightmap.FindHeightmap(pool.Value);
                checks.Add($"불러진 지형에서도 웅덩이가 용암 (Heightmap.IsLava, 칠 {hm?.GetLava(pool.Value) ?? -1f:0.00})", WorkerAI.Lava(pool.Value));
                Quaternion face = Quaternion.LookRotation(Flat(b0 - a0));
                foreach (var npc in new[] { w.Ragnar, w.Floki })
                {
                    npc.Ai.Interrupt();
                    npc.Ai.Queue.Clear();
                    npc.TeleportTo(new Vector3(a0.x, ha, a0.z) + face * new Vector3(npc == w.Ragnar ? 2f : -2f, 0.5f, -2f), face, false);
                    npc.Heal(1000f, false);
                }
                yield return new WaitForSeconds(2f);
                float t0 = w.Ragnar.Ai.LavaTime + w.Floki.Ai.LavaTime;
                int det0 = w.Ragnar.Ai.TarDetours + w.Floki.Ai.TarDetours, deaths0 = w.Ragnar.Deaths + w.Floki.Deaths;
                yield return Tp(player, b0 + Vector3.up * 1f, player.transform.rotation);
                checks.Add($"플레이어가 건너편에 감 ({Utils.DistanceXZ(player.transform.position, b0):0}m, A에서 {Utils.DistanceXZ(a0, b0):0}m)", Utils.DistanceXZ(player.transform.position, b0) < 3f);
                foreach (var npc in new[] { w.Ragnar, w.Floki }) npc.Ai.Follow(player.GetZDOID(), player.GetPlayerName());
                System.Func<WorkerNpc, bool> arrived = n => Utils.DistanceXZ(n.transform.position, player.transform.position) < 6f;
                yield return TestKit.WaitFor(() => arrived(w.Ragnar) && arrived(w.Floki), 150f);
                checks.Add($"용암 건너편 플레이어에게 닿음 ({TestKit.Waited:0}초, 라그나 {Utils.DistanceXZ(w.Ragnar.transform.position, player.transform.position):0}m·플로키 {Utils.DistanceXZ(w.Floki.transform.position, player.transform.position):0}m)", TestKit.Ok);
                float lava = w.Ragnar.Ai.LavaTime + w.Floki.Ai.LavaTime - t0;
                TestRunner.Log($"다) 용암 비켜 감 {w.Ragnar.Ai.TarDetours + w.Floki.Ai.TarDetours - det0}번, 둘레 길 {w.Ragnar.Ai.HazardPaths + w.Floki.Ai.HazardPaths}번(못 찾음 {w.Ragnar.Ai.HazardPathFails + w.Floki.Ai.HazardPathFails}), 용암 위 {lava:0.0}초");
                checks.Add($"용암을 가로지르지 않음 (용암 위 {lava:0.0}초)", lava < 1f);
                checks.Add("용암 길에서 안 죽음", w.Ragnar.Deaths + w.Floki.Deaths == deaths0);
                foreach (var npc in new[] { w.Ragnar, w.Floki }) npc.Ai.GoHome(false);
                yield return Tp(player, w.Home + Vector3.up * 0.5f, player.transform.rotation, true);
            }
            finally { s_keep = false; }
        }

        /// <summary>
        /// 라) 불꽃금속 광석: 다) 뒤(웅덩이 둘레 구역이 생긴 뒤) 거점 700m 안 LeviathanLava·MineRock_Meteorite를 세고(단계·용암 위인지),
        /// 맞는 곡괭이로 "!FlametalOreNew 3"를 지금 GatherJob 그대로 → 거점 상자에. 용암 위 시간·죽음도 본다.
        /// </summary>
        static IEnumerator Flametal(TestKit.World w, GatherTest.Checks checks)
        {
            if (w.Floki == null) yield break; // 다) 뒤엔 거점 구역이 내려가 w.Chest가 비어 있을 수 있음 — 셈은 거점 자리로
            foreach (var v in ZoneSystem.instance.m_vegetation)
                if (v.m_prefab != null && (v.m_prefab.name == "LeviathanLava" || v.m_prefab.name == "MineRock_Meteorite"))
                    TestRunner.Log($"라) 식생 {v.m_name}: {v.m_prefab.name} 켜짐 {v.m_enable}, 바이옴 {v.m_biome}·{v.m_biomeArea}, 구역당 {v.m_min}~{v.m_max}, 무리 {v.m_groupSizeMin}~{v.m_groupSizeMax}, " +
                        $"높이 {v.m_minAltitude}~{v.m_maxAltitude}, 식생 칠 {v.m_minVegetation}~{v.m_maxVegetation}, 기울기 {v.m_minTilt}~{v.m_maxTilt}, 중심 거리 {v.m_minDistanceFromCenter}~{v.m_maxDistanceFromCenter}, 숲 {v.m_inForest}");
            ZDO best = Sources(w.Home, 700f, "거점 700m", out int tier);
            if (best == null)
            {
                // 가까운 구역에 없으면(LeviathanLava 식생은 꺼져 있음) 이름에 meteor·flametal·leviathan이 든 장소 중 가장 가까운 곳에 가서 센다.
                var names = new Dictionary<string, int>();
                Vector3? loc = null;
                float ld = float.MaxValue;
                foreach (var li in ZoneSystem.instance.GetLocationList())
                {
                    string ln = li.m_location?.m_prefabName ?? "";
                    string low = ln.ToLowerInvariant();
                    if (!low.Contains("meteor") && !low.Contains("flametal") && !low.Contains("leviathan")) continue;
                    names.TryGetValue(ln, out int n); names[ln] = n + 1;
                    float d = Utils.DistanceXZ(w.Home, li.m_position);
                    if (d < ld) { ld = d; loc = li.m_position; }
                }
                var np = new List<string>();
                foreach (var kv in names) np.Add(kv.Key + " " + kv.Value);
                TestRunner.Log($"라) 장소 [{string.Join(", ", np.ToArray())}], 가장 가까운 {(loc == null ? "-" : ld.ToString("0") + "m " + loc.Value)}");
                if (loc != null)
                {
                    Player player = Player.m_localPlayer;
                    yield return Tp(player, loc.Value + Vector3.up * 3f, player.transform.rotation, true);
                    yield return new WaitForSeconds(5f);
                    ZDO far = Sources(loc.Value, 100f, "그 장소 100m", out int t2);
                    checks.Add($"불꽃금속 광석 출처를 찾음 (거점 {ld:0}m 장소)", far != null);
                    if (far != null) yield return FarFlametal(w, checks, far, t2);
                    yield return Tp(player, w.Home + Vector3.up * 0.5f, player.transform.rotation, true);
                    yield break;
                }
            }
            checks.Add("불꽃금속 광석 출처를 찾음 (거점 700m)", best != null);
            if (best == null) yield break;
            string tool = ToolFor(ToolKind.Pickaxe, tier);
            checks.Add($"불꽃금속에 맞는 곡괭이 있음 ({tool ?? "없음"}, 단계 {tier})", tool != null);
            if (tool == null) yield break;
            foreach (var npc in new[] { w.Ragnar, w.Floki })
            {
                npc.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab(tool), 1);
                Prep.WearBest(npc);
                npc.Heal(1000f, false);
            }
            s_keep = true;
            Plugin.Instance.StartCoroutine(TestKit.KeepSiteClear(w.Home, () => s_keep));
            Plugin.Instance.StartCoroutine(TestKit.KeepSiteClear(best.GetPosition(), () => s_keep));
            try
            {
                TestKit.ClearBaseStock(w, "FlametalOreNew");
                float t0 = w.Ragnar.Ai.LavaTime + w.Floki.Ai.LavaTime;
                int deaths0 = w.Ragnar.Deaths + w.Floki.Deaths;
                GatherTest.Say("!FlametalOreNew 3", null);
                yield return TestKit.WaitFor(() => GatherTest.CountBase(w.Home, "FlametalOreNew") >= 3, 480f);
                checks.Add($"불꽃금속 광석 3개가 상자에 ({GatherTest.CountBase(w.Home, "FlametalOreNew")}개, {TestKit.Waited:0}초)", TestKit.Ok);
                float lava = w.Ragnar.Ai.LavaTime + w.Floki.Ai.LavaTime - t0;
                TestRunner.Log($"라) 둘레 길 {w.Ragnar.Ai.HazardPaths + w.Floki.Ai.HazardPaths}번(못 찾음 {w.Ragnar.Ai.HazardPathFails + w.Floki.Ai.HazardPathFails}), 용암 위 {lava:0.0}초, 일 {w.Ragnar.Ai.Job?.GetType().Name ?? "-"}");
                checks.Add($"광석 캐며 용암 위 오래 안 있음 ({lava:0.0}초)", lava < 3f);
                checks.Add("불꽃금속 캐며 안 죽음", w.Ragnar.Deaths + w.Floki.Deaths == deaths0);
                TestKit.ClearBaseStock(w, "FlametalOreNew");
            }
            finally { s_keep = false; }
        }


        /// <summary>
        /// 라-2) 먼 불꽃금속: 바위에서 가장 가까운 마른 땅(용암 아님, 8~40m)에 플레이어·일꾼을 두고 맞는 곡괭이로 "!FlametalOreNew 3".
        /// 바위가 용암 한가운데면 "용암 한가운데라 못 가"(lavaOut)를 말하고 그만두는지, 용암을 안 밟고 안 죽는지(decisions 회차 225).
        /// </summary>
        static IEnumerator FarFlametal(TestKit.World w, GatherTest.Checks checks, ZDO rock, int tier)
        {
            Vector3 r = rock.GetPosition();
            Vector3? dry = Dry(r, 8f);
            checks.Add($"불꽃금속 바위 둘레 마른 땅 ({(dry == null ? "-" : Utils.DistanceXZ(dry.Value, r).ToString("0") + "m")})", dry != null);
            if (dry == null) yield break;
            string tool = ToolFor(ToolKind.Pickaxe, tier);
            checks.Add($"불꽃금속에 맞는 곡괭이 있음 ({tool ?? "없음"}, 단계 {tier})", tool != null);
            if (tool == null) yield break;
            Player player = Player.m_localPlayer;
            yield return Tp(player, dry.Value + Vector3.up * 1f, player.transform.rotation);
            s_keep = true;
            Plugin.Instance.StartCoroutine(TestKit.KeepSiteClear(dry.Value, () => s_keep));
            try
            {
                Quaternion face = Quaternion.LookRotation(Flat(r - dry.Value));
                foreach (var npc in new[] { w.Ragnar, w.Floki })
                {
                    npc.Ai.Interrupt();
                    npc.Ai.Queue.Clear();
                    npc.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab(tool), 1);
                    Prep.WearBest(npc);
                    npc.TeleportTo(dry.Value + face * new Vector3(npc == w.Ragnar ? 2f : -2f, 0.5f, -2f), face, false);
                    npc.Heal(1000f, false);
                }
                yield return new WaitForSeconds(2f);
                float t0 = w.Ragnar.Ai.LavaTime + w.Floki.Ai.LavaTime;
                int deaths0 = w.Ragnar.Deaths + w.Floki.Deaths, outs0 = GatherJob.LavaOuts;
                GatherTest.Say("!FlametalOreNew 3", null);
                yield return TestKit.WaitFor(() => GatherJob.LavaOuts > outs0, 90f);
                checks.Add($"용암 한가운데 바위는 \"못 가\" 말함 ({TestKit.Waited:0}초)", TestKit.Ok);
                yield return TestKit.WaitFor(() => w.Ragnar.Ai.Job == null && w.Floki.Ai.Job == null, 30f);
                float lava = w.Ragnar.Ai.LavaTime + w.Floki.Ai.LavaTime - t0;
                TestRunner.Log($"라-2) 바위 칠 {Heightmap.FindHeightmap(r)?.GetLava(r) ?? -1f:0.00}, 마른 땅 {Utils.DistanceXZ(dry.Value, r):0}m, 용암 위 {lava:0.0}초, 일 라그나 {w.Ragnar.Ai.Job?.GetType().Name ?? "-"}·플로키 {w.Floki.Ai.Job?.GetType().Name ?? "-"}");
                checks.Add("못 캐는 불꽃금속 일은 그만둠", TestKit.Ok);
                checks.Add($"불꽃금속 보러 가며 용암 위 오래 안 있음 ({lava:0.0}초)", lava < 1f);
                checks.Add("불꽃금속 보러 가며 안 죽음", w.Ragnar.Deaths + w.Floki.Deaths == deaths0);
                foreach (var npc in new[] { w.Ragnar, w.Floki }) npc.Ai.GoHome(false);
            }
            finally { s_keep = false; }
        }

        /// <summary>r에서 가장 가까운 마른 땅(min~40m, 3m 둘레도 용암 아님, 물보다 0.5m 높음, 불러진 지형).</summary>
        static Vector3? Dry(Vector3 r, float min)
        {
            for (float d = min; d <= 40f; d += 2f)
                for (int k = 0; k < 24; k++)
                {
                    Vector3 p = r + new Vector3(Mathf.Cos(k * Mathf.PI / 12f), 0f, Mathf.Sin(k * Mathf.PI / 12f)) * d;
                    bool ok = !WorkerAI.Lava(p) && Heightmap.FindHeightmap(p) != null;
                    for (int j = 0; j < 8 && ok; j++) ok = !WorkerAI.Lava(p + new Vector3(Mathf.Cos(j * Mathf.PI / 4f), 0f, Mathf.Sin(j * Mathf.PI / 4f)) * 3f);
                    if (ok && ZoneSystem.instance.GetGroundHeight(p, out float h) && h > ZoneSystem.instance.m_waterLevel + 0.5f) { p.y = h; return p; }
                }
            return null;
        }

        /// <summary>
        /// 마-3) 유황: Pickable_SulfurRock 식생 조건(바이옴·높이·식생 칠 = 칠 마스크 a, ZoneSystem.PlaceVegetation)을 diag에 적고,
        /// 거점 2km 안 그 조건에 맞는 애쉬랜드 자리(64m 격자, WorldGenerator 마스크)로 가까운 순 3곳까지 가서 100m 안 유황 바위를 센다.
        /// 찾으면 가장 가까운 마른 땅에 일꾼을 두고 "!SulfurStone 2" — 주워 오거나(일꾼 가방+거점 상자) 용암 한가운데면 "못 가" 말하는지, 용암 위·죽음.
        /// </summary>
        static IEnumerator FarSulfur(TestKit.World w, GatherTest.Checks checks)
        {
            if (w.Floki == null) yield break;
            var vegs = new List<ZoneSystem.ZoneVegetation>();
            foreach (var v in ZoneSystem.instance.m_vegetation)
                if (v.m_prefab != null && (v.m_prefab.name == "Pickable_SulfurRock" ||
                    System.Array.Exists(v.m_prefab.GetComponentsInChildren<Pickable>(true), k => k.m_itemPrefab != null && k.m_itemPrefab.name == "SulfurStone")))
                {
                    TestRunner.Log($"마-3) 식생 {v.m_name}: 켜짐 {v.m_enable}, 바이옴 {v.m_biome}·{v.m_biomeArea}, 구역당 {v.m_min}~{v.m_max}, 무리 {v.m_groupSizeMin}~{v.m_groupSizeMax}, " +
                        $"높이 {v.m_minAltitude}~{v.m_maxAltitude}, 식생 칠 {v.m_minVegetation}~{v.m_maxVegetation}, 기울기 {v.m_minTilt}~{v.m_maxTilt}, 정적 표면 {v.m_snapToStaticSolid}");
                    if (v.m_enable) vegs.Add(v);
                }
            var spots = new List<Vector3>();
            if (vegs.Count == 0)
            {
                // 식생에 없으면 장소에서: 이름에 sulfur가 든 장소, 없으면 종류가 다른 가까운 애쉬랜드 장소 8곳(장소 이름을 diag에).
                var names = new Dictionary<string, int>();
                var near = new Dictionary<string, Vector3>();
                foreach (var li in ZoneSystem.instance.GetLocationList())
                {
                    if (li.m_location == null || (li.m_location.m_biome & Heightmap.Biome.AshLands) == 0) continue;
                    string ln = li.m_location.m_prefabName ?? "";
                    names.TryGetValue(ln, out int n); names[ln] = n + 1;
                    if (!near.TryGetValue(ln, out Vector3 q) || Utils.DistanceXZ(w.Home, li.m_position) < Utils.DistanceXZ(w.Home, q)) near[ln] = li.m_position;
                }
                var np = new List<string>();
                foreach (var kv in names) np.Add($"{kv.Key} {kv.Value}({Utils.DistanceXZ(w.Home, near[kv.Key]):0}m)");
                TestRunner.Log($"마-3) 유황 식생 없음 — 애쉬랜드 장소 [{string.Join(", ", np.ToArray())}]");
                foreach (var kv in near) if (kv.Key.ToLowerInvariant().Contains("sulfur")) spots.Add(kv.Value);
                if (spots.Count == 0) spots.AddRange(near.Values);
            }
            else
            for (float x = -2000f; x <= 2000f; x += 64f)
                for (float z = -2000f; z <= 2000f; z += 64f)
                {
                    Vector3 p = w.Home + new Vector3(x, 0f, z);
                    if (WorldGenerator.instance.GetBiome(p) != Heightmap.Biome.AshLands) continue;
                    float alt = WorldGenerator.instance.GetHeight(p.x, p.z, out Color m) - 30f;
                    foreach (var v in vegs)
                        if (alt >= v.m_minAltitude && alt <= v.m_maxAltitude && (v.m_minVegetation.Equals(v.m_maxVegetation) || (m.a >= v.m_minVegetation && m.a <= v.m_maxVegetation)))
                        { p.y = alt + 30f; spots.Add(p); break; }
                }
            spots.Sort((a, b) => Utils.DistanceXZ(w.Home, a).CompareTo(Utils.DistanceXZ(w.Home, b)));
            TestRunner.Log($"마-3) 조건 맞는 자리 2km 안 {spots.Count}곳, 가장 가까운 {(spots.Count == 0 ? "-" : Utils.DistanceXZ(w.Home, spots[0]).ToString("0") + "m")}");
            Player player = Player.m_localPlayer;
            string sname = ObjectDB.instance.GetItemPrefab("SulfurStone").GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
            ZDO rock = null;
            var tried = new List<Vector3>();
            foreach (var p in spots)
            {
                if (tried.Count >= (vegs.Count > 0 ? 3 : 8) || rock != null) break;
                if (tried.Exists(t => Utils.DistanceXZ(t, p) < 200f)) continue;
                tried.Add(p);
                yield return Tp(player, p + Vector3.up * 3f, player.transform.rotation, true);
                yield return new WaitForSeconds(5f);
                var src = Senses.Nearby(p, 100f, z => ZNetScene.instance.GetPrefab(z.GetPrefab())?.name == "Pickable_SulfurRock" && !z.GetBool(ZDOVars.s_picked));
                int onLava = 0;
                float bd = float.MaxValue;
                foreach (var z in src)
                {
                    if (WorkerAI.Lava(z.GetPosition())) onLava++;
                    float d = Utils.DistanceXZ(p, z.GetPosition());
                    if (d < bd) { bd = d; rock = z; }
                }
                TestRunner.Log($"마-3) 거점 {Utils.DistanceXZ(w.Home, p):0}m 자리 100m 안 유황 {src.Count}개(용암 위 {onLava})" +
                    (rock == null ? "" : $", 가장 가까운 {bd:0}m 칠 {Heightmap.FindHeightmap(rock.GetPosition())?.GetLava(rock.GetPosition()) ?? -1f:0.00} 높이 {rock.GetPosition().y:0}"));
            }
            checks.Add($"유황 바위를 찾음 ({tried.Count}곳 봄)", rock != null);
            if (rock != null)
            {
                Vector3 r = rock.GetPosition();
                Vector3? dry = Dry(r, 4f);
                checks.Add($"유황 바위 둘레 마른 땅 ({(dry == null ? "-" : Utils.DistanceXZ(dry.Value, r).ToString("0") + "m")})", dry != null);
                if (dry != null)
                {
                    yield return Tp(player, dry.Value + Vector3.up * 1f, player.transform.rotation);
                    s_keep = true;
                    Plugin.Instance.StartCoroutine(TestKit.KeepSiteClear(dry.Value, () => s_keep));
                    try
                    {
                        Quaternion face = Quaternion.LookRotation(Flat(r - dry.Value));
                        foreach (var npc in new[] { w.Ragnar, w.Floki })
                        {
                            npc.Ai.Interrupt();
                            npc.Ai.Queue.Clear();
                            npc.GetInventory().RemoveItem(sname, 999);
                            npc.TeleportTo(dry.Value + face * new Vector3(npc == w.Ragnar ? 2f : -2f, 0.5f, -2f), face, false);
                            npc.Heal(1000f, false);
                        }
                        yield return new WaitForSeconds(2f);
                        TestKit.ClearBaseStock(w, "SulfurStone");
                        float t0 = w.Ragnar.Ai.LavaTime + w.Floki.Ai.LavaTime;
                        int deaths0 = w.Ragnar.Deaths + w.Floki.Deaths, outs0 = GatherJob.LavaOuts;
                        System.Func<int> got = () => GatherTest.CountBase(w.Home, "SulfurStone") + w.Ragnar.GetInventory().CountItems(sname) + w.Floki.GetInventory().CountItems(sname);
                        GatherTest.Say("!SulfurStone 2", null);
                        yield return TestKit.WaitFor(() => got() >= 2 || GatherJob.LavaOuts > outs0, 300f);
                        bool lavaOut = GatherJob.LavaOuts > outs0;
                        checks.Add($"유황 2개 주움 또는 용암 한가운데라 \"못 가\" ({(lavaOut ? "못 가" : got() + "개")}, {TestKit.Waited:0}초)", TestKit.Ok);
                        float lava = w.Ragnar.Ai.LavaTime + w.Floki.Ai.LavaTime - t0;
                        TestRunner.Log($"마-3) 유황 마른 땅 {Utils.DistanceXZ(dry.Value, r):0}m, 주움 {got()}, 못 가 {lavaOut}, 용암 위 {lava:0.0}초, 일 {w.Ragnar.Ai.Job?.GetType().Name ?? "-"}");
                        checks.Add($"유황 보러 가며 용암 위 오래 안 있음 ({lava:0.0}초)", lava < 1f);
                        checks.Add("유황 보러 가며 안 죽음", w.Ragnar.Deaths + w.Floki.Deaths == deaths0);
                        foreach (var npc in new[] { w.Ragnar, w.Floki })
                        {
                            npc.Ai.Queue.Clear();
                            npc.Ai.Interrupt();
                            npc.GetInventory().RemoveItem(sname, 999);
                        }
                    }
                    finally { s_keep = false; }
                }
            }
            foreach (var npc in new[] { w.Ragnar, w.Floki })
            {
                npc.Ai.GoHome(false);
                npc.TeleportTo(w.Home + new Vector3(npc == w.Ragnar ? 2f : -2f, 0.5f, 2f), npc.transform.rotation, false);
            }
            yield return Tp(player, w.Home + Vector3.up * 0.5f, player.transform.rotation, true);
        }

        /// <summary>center 둘레 불꽃금속 출처를 세어 diag에(종류·단계·용암 위·가장 가까운 것의 칠), 가장 가까운 것을 돌려줌.</summary>
        static ZDO Sources(Vector3 center, float range, string label, out int tier)
        {
            var srcs = Senses.Nearby(center, range, z => ResourceCatalog.Get(z.GetPrefab())?.Produces.Contains("FlametalOreNew") == true);
            int onLava = 0;
            tier = 0;
            ZDO best = null;
            float bd = float.MaxValue;
            var kinds = new Dictionary<string, int>();
            foreach (var z in srcs)
            {
                tier = Mathf.Max(tier, ResourceCatalog.Get(z.GetPrefab()).MinToolTier);
                string name = ZNetScene.instance.GetPrefab(z.GetPrefab())?.name ?? "?";
                kinds.TryGetValue(name, out int n); kinds[name] = n + 1;
                if (WorkerAI.Lava(z.GetPosition())) onLava++;
                float d = Utils.DistanceXZ(center, z.GetPosition());
                if (d < bd) { bd = d; best = z; }
            }
            var parts = new List<string>();
            foreach (var kv in kinds) parts.Add(kv.Key + " " + kv.Value);
            TestRunner.Log($"라) 불꽃금속 출처 {label} 안 {srcs.Count}개 [{string.Join(", ", parts.ToArray())}], 단계 {tier}, 용암 위 {onLava}, 가장 가까운 {(best == null ? "-" : bd.ToString("0") + "m")}" +
                (best == null ? "" : $" (칠 {Heightmap.FindHeightmap(best.GetPosition())?.GetLava(best.GetPosition()) ?? -1f:0.00}, 높이 {best.GetPosition().y:0})"));
            return best;
        }
        /// <summary>웅덩이 가운데에서 20~45m 양쪽(150도 넘게 벌어짐)에 8m 둘레가 용암 아니고 물보다 1m 높고 평평한(2m 안) 자리, A→B 곧은 선이 용암을 지남.</summary>
        static bool Spots(Vector3 pool, out Vector3 a0, out Vector3 b0, List<Vector3[]> all = null)
        {
            a0 = b0 = Vector3.zero;
            var dry = new List<Vector3>();
            float wl = ZoneSystem.instance.m_waterLevel;
            for (float r = 20f; r <= 45f; r += 5f)
                for (int k = 0; k < 24; k++)
                {
                    Vector3 p = pool + new Vector3(Mathf.Cos(k * Mathf.PI / 12f), 0f, Mathf.Sin(k * Mathf.PI / 12f)) * r;
                    if (WorldGenerator.instance.GetBiome(p) != Heightmap.Biome.AshLands) continue;
                    float h = WorldGenerator.instance.GetHeight(p.x, p.z);
                    if (h < wl + 1f) continue;
                    bool ok = true;
                    for (int j = 0; j < 9 && ok; j++)
                    {
                        Vector3 q = j == 0 ? p : p + new Vector3(Mathf.Cos(j * Mathf.PI / 4f), 0f, Mathf.Sin(j * Mathf.PI / 4f)) * 8f;
                        ok = !GenLava(q, WorkerAI.LavaEdge) && Mathf.Abs(WorldGenerator.instance.GetHeight(q.x, q.z) - h) < 2f;
                    }
                    if (ok) { p.y = h; dry.Add(p); }
                }
            foreach (var a in dry)
                foreach (var b in dry)
                {
                    if (Vector3.Angle(Flat(a - pool), Flat(b - pool)) < 150f) continue;
                    bool crosses = false;
                    for (float s = 0f; s <= 1f && !crosses; s += 0.02f) crosses = GenLava(Vector3.Lerp(a, b, s));
                    if (!crosses) continue;
                    if (all != null) { all.Add(new[] { a, b }); break; } // A마다 B 하나
                    a0 = a; b0 = b; return true;
                }
            return all != null && all.Count > 0;
        }

        /// <summary>플레이어 순간이동(TarTest와 같음): 받아질 때까지 다시 부르고 끝날 때까지 기다림.</summary>
        static IEnumerator Tp(Player player, Vector3 pos, Quaternion rot, bool distant = false)
        {
            yield return TestKit.WaitFor(() => player.TeleportTo(pos, rot, distant), 10f);
            yield return TestKit.WaitFor(() => !player.IsTeleporting(), 60f);
            yield return new WaitForSeconds(distant ? 5f : 1f);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
        static bool s_keep;

        /// <summary>VH_BIOME=ashlands: 자리가 애쉬랜드인지, 둘레 100m 용암 칸 비율(Heightmap.IsLava, 4m 간격)과 150m 안 자원을 diag에(나·다 조각 바탕).</summary>
        static void Site(TestKit.World w, GatherTest.Checks checks)
        {
            var biome = WorldGenerator.instance.GetBiome(w.Home);
            checks.Add($"시험 자리가 애쉬랜드 (바이옴 {biome})", biome == Heightmap.Biome.AshLands);
            int lava = 0, all = 0;
            for (float x = -100f; x <= 100f; x += 4f)
                for (float z = -100f; z <= 100f; z += 4f)
                {
                    Vector3 at = w.Home + new Vector3(x, 0f, z);
                    Heightmap hm = Heightmap.FindHeightmap(at);
                    if (hm == null) continue;
                    all++;
                    if (hm.IsLava(at)) lava++;
                }
            TestRunner.Log($"애쉬랜드 자리 100m 안 용암 칸 {lava}/{all}");
            var kinds = new Dictionary<string, int>();
            foreach (var z in Senses.Nearby(w.Home, 150f, z => ResourceCatalog.Get(z.GetPrefab()) != null))
                foreach (string item in ResourceCatalog.Get(z.GetPrefab()).Produces) { kinds.TryGetValue(item, out int n); kinds[item] = n + 1; }
            var parts = new List<string>();
            foreach (var kv in kinds) parts.Add(kv.Key + " " + kv.Value);
            TestRunner.Log($"애쉬랜드 자리 150m 안 자원: {string.Join(", ", parts.ToArray())}");
        }
    }
}
