using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// vh_deepnorthtest (⑮ 딥노스 1조각): 프리팹 이름을 모르므로 게임 생성 표에서 딥노스 것만 뽑아 diag에 남긴다 —
    /// 적(SpawnSystem 켜진 것, 바이옴 비트 DeepNorth)·한 마리 위험 비율·떨굼, 식생(ZoneSystem.m_vegetation)과 그 물건을 얻는 길, 장소(m_locations).
    /// 다음 조각(깊은 눈·채집·싸움·출발 판단)의 바탕 자료 — 판정은 표에 딥노스 것이 있는지·데이터로 셀 수 있는지만.
    /// </summary>
    static class DeepNorthTest
    {
        public static void Register() => TestRunner.Register("vh_deepnorthtest", "딥노스 자료 시험", () => Run(), 600f);

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

        const Heightmap.Biome DN = Heightmap.Biome.DeepNorth;

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC", w.Ragnar != null);
            if (w.Ragnar == null) yield break;

            var items = new SortedSet<string>();
            var foes = new SortedSet<string>();
            SpawnSystem ss = ZoneSystem.instance.m_zoneCtrlPrefab != null ? ZoneSystem.instance.m_zoneCtrlPrefab.GetComponentInChildren<SpawnSystem>(true) : null;
            if (ss != null)
                foreach (SpawnSystemList list in ss.m_spawnLists)
                    foreach (SpawnSystem.SpawnData d in list.m_spawners)
                        if (d.m_enabled && d.m_prefab != null && (d.m_biome & DN) != 0) foes.Add(d.m_prefab.name);

            float hp = w.Ragnar.GetMaxHealth(), hit = Guard.HitOf(w.Ragnar);
            var taken = Guard.TakenFor(w.Ragnar);
            TestRunner.Log($"시험 일꾼: 체력 {hp:0}, 한 방 {hit:0.0}, 갑옷 {w.Ragnar.GetBodyArmor():0}");
            int counted = 0;
            foreach (string kind in foes)
            {
                Character c = ZNetScene.instance.GetPrefab(kind)?.GetComponent<Character>();
                if (c == null) { TestRunner.Log($"적 {kind}: Character 없음"); continue; }
                Foe f = Guard.Resolve(new GroupFoe { Kind = kind, Count = 1, Level = 1 });
                float ratio = Danger.Ratio(new List<Foe> { f }, hp, hit, taken);
                float data = Guard.DataHit(c);
                if (data > 0f) counted++;
                var drops = new List<string>();
                var cd = c.GetComponent<CharacterDrop>();
                if (cd != null)
                    foreach (var dr in cd.m_drops)
                        if (dr.m_prefab != null) { drops.Add($"{dr.m_prefab.name} {dr.m_amountMin}-{dr.m_amountMax}({dr.m_chance:0.##})"); items.Add(dr.m_prefab.name); }
                TestRunner.Log($"적 {kind}: 진영 {c.m_faction}, 체력 {f.Health:0}, 한 방 {f.Damage:0.0}(데이터 {data:0.0}), 날기 {c.m_flying}, 눈 감속 {c.m_deepSnowSlowMax:0.##}, " +
                    $"위험 비율 {ratio:0.00}, 토박이 {Departures.NativeTo(kind, (int)DN)}, 떨굼 [{string.Join(", ", drops.ToArray())}]");
            }
            checks.Add($"딥노스 생성 표에 적이 있음 ({foes.Count}종)", foes.Count > 0);
            checks.Add($"딥노스 적 한 방을 게임 데이터로 셈 ({counted}/{foes.Count})", foes.Count > 0 && counted > 0);

            int veg = 0;
            foreach (var v in ZoneSystem.instance.m_vegetation)
            {
                if (!v.m_enable || v.m_prefab == null || (v.m_biome & DN) == 0) continue;
                veg++;
                ResourceInfo info = ResourceCatalog.Get(v.m_prefab.name.GetStableHashCode());
                if (info != null) foreach (string it in info.Produces) items.Add(it);
                TestRunner.Log($"식생 {v.m_prefab.name}: 바이옴 {v.m_biome}, 구역당 {v.m_min}~{v.m_max}, 물건 [{(info == null ? "-" : string.Join(",", new List<string>(info.Produces).ToArray()))}]");
            }
            checks.Add($"딥노스 식생이 있음 ({veg}종)", veg > 0);

            var locs = new SortedSet<string>();
            foreach (var l in ZoneSystem.instance.m_locations)
                if (l.m_enable && !string.IsNullOrEmpty(l.m_prefabName) && (l.m_biome & DN) != 0) locs.Add($"{l.m_prefabName}({l.m_quantity})");
            TestRunner.Log($"딥노스 장소 {locs.Count}종: {string.Join(", ", new List<string>(locs).ToArray())}");

            int ways = 0;
            foreach (string item in items)
            {
                var wl = ResourceCatalog.WaysFor(item);
                if (wl.Count > 0) ways++;
                TestRunner.Log($"물건 {item}: 방법 [{string.Join(",", new List<ToolKind>(wl).ConvertAll(t => t.ToString()).ToArray())}], 출처 [{string.Join(", ", ResourceCatalog.SourcesOf(item).ToArray())}]");
            }
            checks.Add($"딥노스 물건을 얻는 길을 셈 ({ways}/{items.Count})", ways > 0);
            if (TestKit.RunBiome != DN) yield break;
            yield return Site(w, checks);
        }

        /// <summary>깊은 눈 높이(m): Character가 쓰는 대로 Heightmap.GetCultivationMask × m_deepSnowSlowMaxHeight. 지형이 없으면 -1.</summary>
        static float Snow(Vector3 at, Character c)
        {
            Heightmap hm = Heightmap.FindHeightmap(at);
            return hm == null ? -1f : hm.GetCultivationMask(at) * c.m_deepSnowSlowMaxHeight;
        }

        /// <summary>
        /// 나) VH_BIOME=deepnorth: 자리가 딥노스인지, 일꾼 눈 감속 값(Player에서 복사)·둘레 100m 깊은 눈 칸, 깊은 눈 쪽으로 플레이어를 따라 걸을 때
        /// 눈 위·밖 속도, 추위(Freezing은 Player.UpdateEnvStatusEffects만 줌 — 일꾼에게 안 붙는지).
        /// </summary>
        static IEnumerator Site(TestKit.World w, GatherTest.Checks checks)
        {
            WorkerNpc npc = w.Ragnar;
            var biome = WorldGenerator.instance.GetBiome(w.Home);
            checks.Add($"시험 자리가 딥노스 (바이옴 {biome})", biome == DN);
            Player player = Player.m_localPlayer;
            TestRunner.Log($"눈 감속: 일꾼 최대 {npc.m_deepSnowSlowMax:0.##}·시작 {npc.m_deepSnowSlowStartHeight:0.##}m·최대 높이 {npc.m_deepSnowSlowMaxHeight:0.##}m, 플레이어 최대 {player.m_deepSnowSlowMax:0.##}, " +
                $"걷기 {npc.m_walkSpeed:0.0}·달리기 {npc.m_runSpeed:0.0}, 얼어붙음 날씨 {EnvMan.IsFreezing()}·추움 {EnvMan.IsCold()}");
            int all = 0, deep = 0;
            float max = 0f;
            Vector3? far = null;
            for (float x = -100f; x <= 100f; x += 4f)
                for (float z = -100f; z <= 100f; z += 4f)
                {
                    Vector3 at = w.Home + new Vector3(x, 0f, z);
                    float s = Snow(at, npc);
                    if (s < 0f) continue;
                    all++;
                    if (s <= npc.m_deepSnowSlowStartHeight) continue;
                    deep++;
                    max = Mathf.Max(max, s);
                    float d = Utils.DistanceXZ(at, w.Home);
                    if (s >= 1f && d >= 25f && d <= 60f && (far == null || Snow(far.Value, npc) < s)) far = at;
                }
            TestRunner.Log($"자리 100m 안 깊은 눈 칸 {deep}/{all}(최고 {max:0.00}m), 거점 눈 {Snow(w.Home, npc):0.00}m");
            checks.Add($"둘레 지형을 읽음 ({all}칸)", all > 0);
            if (far == null) { TestRunner.Log("25~60m 안 1m 넘는 깊은 눈 없음 — 걷기 재기 건너뜀"); yield break; }
            ZoneSystem.instance.GetGroundHeight(far.Value, out float fh);
            Vector3 goal = new Vector3(far.Value.x, fh, far.Value.z);
            s_keep = true;
            Plugin.Instance.StartCoroutine(TestKit.KeepSiteClear(goal, () => s_keep));
            Plugin.Instance.StartCoroutine(TestKit.KeepSiteClear(w.Home, () => s_keep));
            float inT = 0f, inD = 0f, outT = 0f, outD = 0f;
            bool cold = false;
            try
            {
                npc.Ai.Interrupt();
                npc.Ai.Queue.Clear();
                npc.TeleportTo(w.Home + Vector3.up * 0.5f, npc.transform.rotation, false);
                yield return new WaitForSeconds(2f);
                yield return TestKit.WaitFor(() => player.TeleportTo(goal + Vector3.up * 1f, player.transform.rotation, false), 10f);
                yield return TestKit.WaitFor(() => !player.IsTeleporting(), 60f);
                npc.Ai.Follow(player.GetZDOID(), player.GetPlayerName());
                float t0 = Time.time;
                Vector3 last = npc.transform.position;
                while (Time.time - t0 < 90f && Utils.DistanceXZ(npc.transform.position, player.transform.position) > 5f)
                {
                    yield return null;
                    Vector3 now = npc.transform.position;
                    float step = Utils.DistanceXZ(now, last), dt = Time.deltaTime;
                    last = now;
                    if (step > 2f) continue; // 순간이동 칸은 안 셈
                    if (Snow(now, npc) > npc.m_deepSnowSlowStartHeight) { inT += dt; inD += step; } else { outT += dt; outD += step; }
                    cold |= npc.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectFreezing) || npc.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectCold);
                }
                float took = Time.time - t0;
                TestRunner.Log($"깊은 눈({Snow(goal, npc):0.00}m) {Utils.DistanceXZ(goal, w.Home):0}m 걷기: {took:0.0}초, 눈 위 {inD:0.0}m/{inT:0.0}초 = {(inT > 0f ? inD / inT : 0f):0.00}m/s, " +
                    $"눈 밖 {outD:0.0}m/{outT:0.0}초 = {(outT > 0f ? outD / outT : 0f):0.00}m/s");
                checks.Add($"깊은 눈을 지나 플레이어에게 닿음 ({took:0}초)", Utils.DistanceXZ(npc.transform.position, player.transform.position) <= 5f);
                checks.Add("일꾼에게 추위(Cold·Freezing)가 안 붙음", !cold);
            }
            finally { s_keep = false; }
            npc.Ai.GoHome(false);
            yield return TestKit.WaitFor(() => player.TeleportTo(w.Home + Vector3.up * 0.5f, player.transform.rotation, false), 10f);
            yield return TestKit.WaitFor(() => !player.IsTeleporting(), 60f);
        }

        static bool s_keep;
    }
}
