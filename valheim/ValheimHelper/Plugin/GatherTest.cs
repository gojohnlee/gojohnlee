using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 채집 시험(② 단계). 깃발 → 상자 2개 → 한 상자에 도구(상자는 지정하지 않음)
    /// → "!라그나 &lt;자원&gt; 10" → 거점 상자들에 10개 이상 더 들어오는지(최대 6분).
    /// vh_woodtest: 부싯돌 도끼 + 나무. vh_stonetest: 뿔 곡괭이 + 돌(줍기·캐기 둘 다 가능).
    /// 깃발·상자·NPC는 시험 뒤에도 남긴다.
    /// </summary>
    static class GatherTest
    {
        public static void Register()
        {
            TestRunner.Register("vh_woodtest", "나무 채집 시험", () => Run("AxeFlint", "Wood", "나무"));
            TestRunner.Register("vh_stonetest", "돌 채집 시험", () => Run("PickaxeAntler", "Stone", "돌"));
            TestRunner.Register("vh_coppertest", "구리 채집 시험(광맥을 거점 12m 앞에 놓음, 땅속 광석 파기·구덩이 탈출)", () => Run("PickaxeAntler", "CopperOre", "구리", true));
            TestRunner.Register("vh_tintest", "주석 채집 시험(광맥을 거점 12m 앞에 놓음, 구리와 같은 경로)", () => Run("PickaxeAntler", "TinOre", "주석", true));
        }

        internal sealed class Checks
        {
            public int Total, Passed;
            public void Add(string what, bool ok)
            {
                Total++;
                if (ok) Passed++;
                TestRunner.Log($"{(ok ? "OK  " : "FAIL")} {what}");
                if (!ok) TestKit.NoteFail();
            }
        }

        static IEnumerator Run(string toolPrefab, string item, string word, bool spawnDeposit = false)
        {
            var checks = new Checks();
            Player player = Player.m_localPlayer;
            if (player == null || !ZNet.instance.IsServer())
            {
                TestRunner.Log("FAIL: 월드에 들어간 뒤(싱글 또는 서버 시작) 실행해야 합니다.");
                TestRunner.Finish();
                yield break;
            }
            bool god = player.InGodMode();
            player.SetGodMode(true);
            var heard = new List<string>();
            System.Action<string, string> onHeard = (n, t) => heard.Add(n + ": " + t);
            ChatNet.Heard += onHeard;

            var ways = ResourceCatalog.WaysFor(item);
            checks.Add($"자원 목록 {ResourceCatalog.Count}종, {word} 얻는 법 [{string.Join(",", ways)}]", ways.Count > 0);
            GameObject tool = ObjectDB.instance.GetItemPrefab(toolPrefab);
            GameObject chestPrefab = ZNetScene.instance.GetPrefab("piece_chest_wood");
            checks.Add($"{toolPrefab}·상자 프리팹 있음", tool != null && chestPrefab != null && BaseFlag.Prefab != null);
            if (tool == null || chestPrefab == null || BaseFlag.Prefab == null) { End(player, god, checks, onHeard, heard); yield break; }
            string toolToken = tool.GetComponent<ItemDrop>().m_itemData.m_shared.m_name;

            // 1) 깃발과 NPC
            Workers.DespawnAll();
            yield return new WaitForSeconds(1f);
            // 물가에서 돌면 상자가 물속에 놓여 일꾼이 못 간다(루프 회차 5 stonetest: 장비 상자 물 3.1m 아래) → 마른 초원으로 옮긴다
            if (!DrySite(player.transform.position, player.transform.rotation, real: true))
            {
                Vector3 dry = FindDrySite(player.transform.position, player.transform.rotation);
                if (dry != player.transform.position)
                {
                    player.TeleportTo(dry + Vector3.up, player.transform.rotation, true);
                    float wait = 0f;
                    yield return new WaitForSeconds(1f);
                    while (player.IsTeleporting() && wait < 40f) { wait += 0.5f; yield return new WaitForSeconds(0.5f); }
                    yield return new WaitForSeconds(2f);
                    TestRunner.Log($"물가라 마른 땅으로 옮김: 높이 {dry.y:0.0} (물 {ZoneSystem.instance.m_waterLevel:0.0})");
                }
                else TestRunner.Log("마른 땅 못 찾음(300m 안) → 제자리에서");
            }
            Vector3 home = Ground(player.transform.position + player.transform.forward * 4f);
            Quaternion rot = player.transform.rotation;
            if (TestKit.OldClear(home) > 0) yield return new WaitForSeconds(0.5f); // 둘레 생물(적·동료 해골)을 치우고 시작(사용자 결정 2026-10-22 시험 환경)
            TestKit.OldCheck(home, player, god);
            player.PlacePiece(BaseFlag.Prefab.GetComponent<Piece>(), home, rot, doAttack: false);
            yield return new WaitForSeconds(3f);
            WorkerNpc claude = WorkerNpc.All.Find(n => n.Personality == Core.Npc.Personality.Ragnar);
            checks.Add("깃발에 NPC 나타남", claude != null && WorkerNpc.All.Count == 2);
            if (claude == null) { End(player, god, checks, onHeard, heard); yield break; }

            // 2) 상자 2개
            if (GatherTest.ClearOldChests(home) > 0) yield return new WaitForSeconds(0.5f); // 없앤 상자는 프레임 끝에 사라짐: 그 전에 땅 높이를 재면 옛 상자 위에 놓인다
            Piece chestPiece = chestPrefab.GetComponent<Piece>();
            Vector3 eqPos = Ground(home + rot * new Vector3(-4f, 0f, -1f));
            Vector3 resPos = Ground(home + rot * new Vector3(4f, 0f, -1f));
            player.PlacePiece(chestPiece, eqPos, rot, doAttack: false);
            player.PlacePiece(chestPiece, resPos, rot, doAttack: false);
            yield return new WaitForSeconds(1f);
            Container eq = ChestAt(eqPos), res = ChestAt(resPos);
            checks.Add("상자 2개 놓임", eq != null && res != null);
            if (eq == null || res == null) { End(player, god, checks, onHeard, heard); yield break; }
            // 광맥은 오래 캐서 곡괭이가 부러질 수 있다(바닐라 내구도) → 예비 하나 더
            bool put = Chests.TryEdit(eq, inv => { inv.AddItem(tool, 1); if (spawnDeposit) inv.AddItem(tool, 1); });
            checks.Add($"장비 상자에 {toolPrefab}", put && eq.GetInventory().HaveItem(toolToken));

            // 3) 10개
            // 광맥(구리 등)은 시험 월드 근처에 없을 수 있어 거점 12m 앞에 놓는다. 지난 시험의 광맥·구덩이는 먼저 치운다.
            Vector3 depositPos = home + rot * new Vector3(0f, 0f, 12f);
            if (spawnDeposit)
            {
                // 건축물 4m 안의 바위는 일꾼이 안 고른다(Buildings) → 지난 시험들이 남긴 상자·깃발에서 떨어진 곳에 놓는다
                foreach (var off in new[] { new Vector3(0f, 0f, 12f), new Vector3(0f, 0f, 18f), new Vector3(8f, 0f, 14f), new Vector3(-8f, 0f, 14f), new Vector3(0f, 0f, 24f) })
                {
                    depositPos = home + rot * off;
                    if (!Buildings.Near(depositPos, 6f)) break;
                }
                GameObject dep = DepositFor(item);
                checks.Add($"{word} 광맥 프리팹 {(dep != null ? dep.name : "없음")}", dep != null);
                if (dep == null) { End(player, god, checks, onHeard, heard); yield break; }
                int hash = dep.name.GetStableHashCode();
                foreach (var zdo in Senses.Nearby(depositPos, 20f))
                {
                    if (zdo.GetPrefab() != hash) continue;
                    ZNetView v = ZNetScene.instance.FindInstance(zdo);
                    if (v == null) continue;
                    v.ClaimOwnership();
                    ZNetScene.instance.Destroy(v.gameObject);
                }
                int undone = TestTerrain.Reset(depositPos, 10f);
                yield return new WaitForSeconds(0.5f);
                depositPos = Ground(depositPos);
                GameObject firstDep = Object.Instantiate(dep, depositPos, rot);
                // 한 광맥이 10개를 못 주면(주석 MineRock_Tin은 3~4개) 모자라는 만큼 둘레에 더 놓는다 — 안 그러면 일꾼이 나머지를 찾아 먼 구역으로 떠난다(0.3.8 첫 주석 시험)
                int deposits = Mathf.Max(1, Mathf.CeilToInt(10f / Mathf.Max(1, MinYield(dep))));
                int placed = 1;
                foreach (var off in new[] { new Vector3(6f, 0f, 0f), new Vector3(-6f, 0f, 0f), new Vector3(0f, 0f, 6f), new Vector3(0f, 0f, -6f), new Vector3(6f, 0f, 6f), new Vector3(-6f, 0f, 6f) })
                {
                    if (placed >= deposits) break;
                    Vector3 p = depositPos + rot * off;
                    if (Buildings.Near(p, 5f)) continue;
                    Object.Instantiate(dep, Ground(p), rot);
                    placed++;
                }
                TestRunner.Log($"광맥 놓음 {dep.name} {placed}개 (지난 구덩이 꼭짓점 {undone}개 되돌림), 첫 광맥 집에서 {Utils.DistanceXZ(depositPos, home):0.0}m 높이차 {depositPos.y - home.y:0.0}m, 상자 {eqPos}·{resPos}, 집 {home}");
                yield return new WaitForSeconds(1f);
                // 일꾼이 그 광맥을 고를 수 있는지(0.3.8 첫 구리 시험: 12m 앞 광맥을 두고 580m 밖으로 감)
                ResourceInfo info = ResourceCatalog.Get(hash);
                int seen = Senses.Nearby(depositPos, 12f).FindAll(z => z.GetPrefab() == hash).Count;
                var near = new List<string>();
                foreach (var z in Senses.Nearby(depositPos, 4f))
                    if (Buildings.IsPiece(z.GetPrefab())) near.Add($"{ZNetScene.instance.GetPrefab(z.GetPrefab())?.name} {Utils.DistanceXZ(z.GetPosition(), depositPos):0.0}m");
                TestRunner.Log($"광맥 ZDO {seen}개, 목록 정보: 도구 {info?.Tool} 단계 {info?.MinToolTier} (곡괭이 단계 {tool.GetComponent<ItemDrop>().m_itemData.m_shared.m_toolTier}), " +
                               $"나오는 것 [{(info != null ? string.Join(",", info.Produces) : "")}], 4m 안 건축물 [{string.Join(", ", near)}]");
                TestRunner.Log($"  광맥 구조: {DescribeDeposit(dep, 0)}");
                TestRunner.Log($"  광맥 조각 퍼짐: {RockSpread(firstDep, Workers.HomeSpot(home, rot, 0), eqPos, resPos)}");
            }
            // E020: 옆 나무에 높이 걸린 통나무(밑에서 쪼개면 반토막이 머리 위로 떨어짐)는 안 고르는지. 거점 9m 앞에 땅에서 6m 뜬 채 선 통나무를
            // 움직이지 않게(kinematic) 둔다 — 통나무 가산점(30m)으로 가장 먼저 고를 자리라 안 거르면 바로 다가간다.
            ZDOID hungId = ZDOID.None;
            bool hungAimed = false;
            if (item == "Wood")
            {
                GameObject logPrefab = ZNetScene.instance.GetPrefab("beech_log");
                if (logPrefab != null)
                {
                    GameObject hungLog = Object.Instantiate(logPrefab, Ground(home + rot * new Vector3(0f, 0f, 9f)) + Vector3.up * 6f, rot);
                    var body = hungLog.GetComponent<Rigidbody>();
                    if (body != null) body.isKinematic = true;
                    hungId = hungLog.GetComponent<ZNetView>().GetZDO().m_uid;
                }
                checks.Add("높이 걸린 통나무 놓음(beech_log, 땅에서 6m)", !hungId.IsNone());
            }
            int dug0 = NpcPatches.NoDig.Dug, blocked0 = NpcPatches.NoDig.Blocked, tp0 = WorkerAI.StuckTeleports;
            Say($"!{Workers.Name1} {word} 10", null);
            int count = 0, count0 = CountBase(home, item); // 지난 시험이 거점 상자에 남긴 것은 뺀다
            float t = 0f;
            while (t < 360f)
            {
                for (int i = 0; i < 10; i++)
                {
                    yield return new WaitForSeconds(0.5f);
                    if (!hungId.IsNone() && claude != null && claude.Ai.Job is GatherJob gj && gj.TargetId == hungId) hungAimed = true;
                }
                t += 5f;
                count = CountBase(home, item) - count0;
                if ((int)t % 15 == 0)
                {
                    var job = claude ? claude.Ai.Job : null;
                    TestRunner.Log($"[{t:0}s] 상자 {word} {count}, 들고 있는 {word} {(claude ? claude.CountItem(item) : -1)}, 상태 {(claude ? claude.Ai.Current.ToString() : "없음")}/{job?.StepName}, 집에서 {(claude ? Utils.DistanceXZ(claude.Home, claude.transform.position) : -1):0}m, 지식 {Brain.Get().Count}구역");
                    if (claude) Plugin.Log.LogInfo($"  [{t:0}s] 광맥에서 {Utils.DistanceXZ(depositPos, claude.transform.position):0.0}m, 땅에 {item} {GroundCount(depositPos, item)}개, {claude.Ai.MoveState} / {claude.MoveDebug}");
                }
                if (count >= 10 || claude == null || claude.Ai.Current != WorkerAI.Mode.Work) break;
            }
            checks.Add($"거점 상자에 {word} {count}개 더 ({t:0}초)", count >= 10);
            if (!hungId.IsNone()) checks.Add("높이 걸린 통나무는 안 고름(쪼개면 반토막이 머리 위로 떨어짐)", !hungAimed);
            checks.Add("상자 2개 멀쩡함(일꾼 도구질에 안 부서짐)", eq != null && res != null && eq.GetComponent<ZNetView>().IsValid() && res.GetComponent<ZNetView>().IsValid());
            checks.Add($"일하다 죽지 않음(쓰러지는 나무 등, 죽은 횟수 {(claude ? claude.Deaths : -1)})", claude != null && claude.Deaths == 0);
            if (claude != null && !claude.IsDead())
            {
                // E020: 나무·통나무 피해(HitType.Tree)는 일꾼에게 안 들어감 — 통나무 피해 50을 직접 맞혀 봄
                float before = claude.GetHealth();
                int spared = NpcPatches.NoTreeHurt.Spared;
                var treeHit = new HitData { m_hitType = HitData.HitType.Tree, m_point = claude.transform.position, m_dir = UnityEngine.Vector3.forward };
                treeHit.m_damage.m_blunt = 50f;
                claude.Damage(treeHit);
                checks.Add($"나무 피해 안 받음(체력 {before:0} → {claude.GetHealth():0}, 일 동안 막은 것 {spared}번)", claude.GetHealth() >= before && NpcPatches.NoTreeHurt.Spared == spared + 1);
            }
            ItemDrop.ItemData held = claude ? claude.GetCurrentWeapon() : null;
            TestRunner.Log($"1호 손: {(held != null && held.m_dropPrefab != null ? held.m_dropPrefab.name + $" 내구도 {held.m_durability:0}/{held.GetMaxDurability():0}" : "빈손")}, 죽은 횟수(남은 묘비) {(claude ? claude.Graves().Count : -1)}");
            // 땅 파기(2026-10-21 사용자: 돌 캘 땐 땅 파기 자제, 구리는 파더라도 구덩이에서 나와야 함)
            int dug = NpcPatches.NoDig.Dug - dug0;
            TestRunner.Log($"땅 파기: 판 {dug}번, 막음 {NpcPatches.NoDig.Blocked - blocked0}번, 점프 {(claude ? claude.Ai.Jumps : -1)}번, 경사로 곡괭이질 {(claude ? claude.Ai.CarveSwings : -1)}번");
            if (!ResourceCatalog.WorthDigging(item)) checks.Add($"{word}{Core.Npc.Josa.For(word, "은")} 땅을 파지 않음(판 횟수 {dug})", dug == 0);
            if (spawnDeposit && claude != null)
            {
                // 판 구덩이에서 나와 거점으로 돌아오는지(일이 끝나면 집으로 간다)
                float w = 0f;
                for (; w < 60f && Utils.DistanceXZ(claude.Home, claude.transform.position) > 5f; w += 1f) yield return new WaitForSeconds(1f);
                checks.Add($"구덩이에서 나와 거점으로 돌아옴({w:0}초, 집에서 {Utils.DistanceXZ(claude.Home, claude.transform.position):0}m)", Utils.DistanceXZ(claude.Home, claude.transform.position) <= 5f);
            }
            checks.Add($"길이 막혀 순간이동하지 않음({WorkerAI.StuckTeleports - tp0}번)", WorkerAI.StuckTeleports == tp0);
            if (spawnDeposit)
            {
                Vector3 p = claude ? claude.transform.position : depositPos;
                TestRunner.Log($"광맥 둘레 가장 깊은 곳 {Deepest(depositPos, 4f):0.0}m, 1호 위치 집에서 {(claude ? Utils.DistanceXZ(claude.Home, p) : -1):0}m, {(claude ? claude.MoveDebug : "")}");
                // 남은 광맥도 치운다: 다음 시험(crypttest 110m 앞)의 길을 막는다(회차 90: 남은 Rock4_cell 사이에서 지하묘지 가기 막힘)
                int gone = RemoveDeposits(depositPos, DepositFor(item));
                int back = TestTerrain.Reset(depositPos, 10f); // 다음 시험을 위해 구덩이 되돌림
                TestRunner.Log($"구덩이 되돌림: 꼭짓점 {back}개");
                TestRunner.Log($"남은 광맥 조각 {gone}개 지움");
            }
            End(player, god, checks, onHeard, heard);
        }

        /// <summary>거점 앞 시험 광맥(그 프리팹과 깨진 조각) 중 20m 안의 것을 지운다. 지운 수.</summary>
        internal static int RemoveDeposits(Vector3 at, GameObject dep)
        {
            if (dep == null) return 0;
            var hashes = new HashSet<int> { dep.name.GetStableHashCode() };
            var des = dep.GetComponent<Destructible>();
            if (des != null && des.m_spawnWhenDestroyed != null) hashes.Add(des.m_spawnWhenDestroyed.name.GetStableHashCode());
            int n = 0;
            foreach (var zdo in Senses.Nearby(at, 20f))
            {
                if (!hashes.Contains(zdo.GetPrefab())) continue;
                ZNetView v = ZNetScene.instance.FindInstance(zdo);
                if (v != null) { v.ClaimOwnership(); ZNetScene.instance.Destroy(v.gameObject); }
                else ZDOMan.instance.DestroyZDO(zdo);
                n++;
            }
            return n;
        }

        /// <summary>
        /// 진단(E017): 놓은 광맥의 조각(켜진 충돌체) 수·땅 위 수, 뿌리에서 조각 상자 경계가 가장 먼 거리(XZ), 1호 집·상자에서 가장 가까운 조각 상자(XZ, 실제 면보다 가깝게 나옴),
        /// 1호 집에서 뿌리 쪽으로 쏜 광선(발 0.4m·가슴 1.4m)이 처음 맞는 조각까지(실제 면).
        /// </summary>
        static string RockSpread(GameObject rock, Vector3 npcHome, Vector3 eq, Vector3 res)
        {
            if (rock == null) return "없음";
            Vector3 root = rock.transform.position;
            int n = 0, up = 0;
            float far = 0f, dHome = float.MaxValue, dEq = float.MaxValue, dRes = float.MaxValue;
            foreach (var c in rock.GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger) continue;
                n++;
                Bounds b = c.bounds;
                if (ZoneSystem.instance.GetGroundHeight(b.center, out float g) && b.max.y > g + 0.3f) up++;
                foreach (var corner in new[] { new Vector3(b.min.x, 0f, b.min.z), new Vector3(b.min.x, 0f, b.max.z), new Vector3(b.max.x, 0f, b.min.z), new Vector3(b.max.x, 0f, b.max.z) })
                    far = Mathf.Max(far, Utils.DistanceXZ(corner, root));
                float Xz(Vector3 p) { Vector3 q = b.ClosestPoint(new Vector3(p.x, b.center.y, p.z)); return Utils.DistanceXZ(q, p); }
                dHome = Mathf.Min(dHome, Xz(npcHome));
                dEq = Mathf.Min(dEq, Xz(eq));
                dRes = Mathf.Min(dRes, Xz(res));
            }
            Vector3 dir = root - npcHome;
            dir.y = 0f;
            float toRoot = dir.magnitude;
            dir.Normalize();
            string Ray(float h)
            {
                var hits = Physics.RaycastAll(npcHome + Vector3.up * h, dir, toRoot, ~0, QueryTriggerInteraction.Ignore);
                float best = float.MaxValue;
                foreach (var hit in hits)
                    if (hit.collider.transform.IsChildOf(rock.transform)) best = Mathf.Min(best, hit.distance);
                return best == float.MaxValue ? "안 맞음" : best.ToString("0.0") + "m";
            }
            return $"조각 {n}개(땅 위 {up}), 뿌리에서 가장 먼 조각 경계 {far:0.0}m, 1호 집에서 뿌리 {toRoot:0.0}m, 가장 가까운 조각 경계까지 1호 집 {dHome:0.0}m·장비 상자 {dEq:0.0}m·재료 상자 {dRes:0.0}m, " +
                   $"1호 집→뿌리 광선 첫 조각 발 {Ray(0.4f)}·가슴 {Ray(1.4f)}";
        }

        /// <summary>진단: 광맥 프리팹이 부서지면 무엇이 몇 개 나오는지(Destructible → 조각 → ...).</summary>
        static string DescribeDeposit(GameObject p, int depth)
        {
            if (p == null || depth > 4) return "없음";
            var sb = new System.Text.StringBuilder(p.name + "{");
            var des = p.GetComponent<Destructible>();
            if (des != null) sb.Append($"Destructible 체력 {des.m_health} 단계 {des.m_minToolTier}, 부서지면 {DescribeDeposit(des.m_spawnWhenDestroyed, depth + 1)}");
            var rock5 = p.GetComponent<MineRock5>();
            if (rock5 != null) sb.Append($"MineRock5 체력 {rock5.m_health}");
            var rock = p.GetComponent<MineRock>();
            if (rock != null) sb.Append($"MineRock 체력 {rock.m_health}");
            var drop = p.GetComponent<DropOnDestroyed>();
            if (drop != null)
            {
                var t = drop.m_dropWhenDestroyed;
                sb.Append($", 떨굼 {t.m_dropMin}~{t.m_dropMax}번(각각 {t.m_oneOfEach}, 확률 {t.m_dropChance}) [");
                foreach (var d in t.m_drops) sb.Append($"{(d.m_item != null ? d.m_item.name : "?")} {d.m_stackMin}~{d.m_stackMax} 가중 {d.m_weight} ");
                sb.Append("]");
            }
            return sb.Append("}").ToString();
        }

        /// <summary>광맥 하나가 부서지며 최소 몇 개를 주는지(Destructible + DropOnDestroyed). 모르면 10(= 하나로 충분).</summary>
        static int MinYield(GameObject p)
        {
            var drop = p.GetComponent<Destructible>() != null ? p.GetComponent<DropOnDestroyed>() : null;
            var t = drop?.m_dropWhenDestroyed;
            if (t == null || t.m_drops.Count == 0 || t.m_dropChance < 1f) return 10;
            int minStack = int.MaxValue;
            foreach (var d in t.m_drops) minStack = Mathf.Min(minStack, d.m_stackMin);
            return Mathf.Max(1, t.m_dropMin * minStack);
        }

        /// <summary>진단: 광맥 둘레 땅에 떨어진 그 물건 수.</summary>
        internal static int GroundCount(Vector3 center, string item)
        {
            int n = 0;
            foreach (var zdo in Senses.Nearby(center, 30f))
            {
                ZNetView v = ZNetScene.instance.FindInstance(zdo);
                ItemDrop d = v != null ? v.GetComponent<ItemDrop>() : null;
                if (d != null && Utils.GetPrefabName(d.gameObject) == item) n += d.m_itemData.m_stack;
            }
            return n;
        }

        /// <summary>그 물건이 나오는 광맥 프리팹(MineRock5 → MineRock 순, 이름에 frac 없는 것 먼저).</summary>
        static GameObject DepositFor(string item)
        {
            GameObject best = null;
            int bestScore = -1;
            foreach (var p in ZNetScene.instance.m_prefabs)
            {
                if (p == null) continue;
                DropTable drops = p.GetComponent<MineRock5>()?.m_dropItems ?? p.GetComponent<MineRock>()?.m_dropItems;
                if (drops?.m_drops == null || !drops.m_drops.Exists(d => d.m_item != null && d.m_item.name == item)) continue;
                int score = (p.GetComponent<MineRock5>() != null ? 2 : 0) + (p.name.Contains("frac") ? 0 : 1);
                TestRunner.Log($"  {item} 광맥 후보: {p.name} (점수 {score})");
                if (score > bestScore) { bestScore = score; best = p; }
            }
            if (best == null)
            {
                // 주석 등은 MineRock이 아니라 Destructible(부서지면 광맥 조각) → 목록이 곡괭이로 얻는다고 아는 프리팹, 이름에 frac·scrap 없는 것 먼저
                foreach (var p in ZNetScene.instance.m_prefabs)
                {
                    if (p == null) continue;
                    ResourceInfo ri = ResourceCatalog.Get(p.name.GetStableHashCode());
                    if (ri == null || ri.Tool != ToolKind.Pickaxe || ri.Pickable || !ri.Produces.Contains(item)) continue;
                    int score = (p.name.Contains("frac") || p.name.Contains("scrap") ? 0 : 100) - ri.Produces.Count; // 통·솥처럼 여러 가지가 나오는 것보다 그것만 나오는 광맥
                    TestRunner.Log($"  {item} 광맥 후보(목록): {p.name} (점수 {score})");
                    if (score > bestScore) { bestScore = score; best = p; }
                }
            }
            return best;
        }

        /// <summary>둘레 r(m) 안에서 원래 땅보다 가장 깊이 파인 정도.</summary>
        internal static float Deepest(Vector3 c, float r)
        {
            float worst = 0f;
            for (float x = -r; x <= r; x += 1f)
                for (float z = -r; z <= r; z += 1f)
                {
                    Vector3 p = c + new Vector3(x, 0f, z);
                    worst = Mathf.Max(worst, WorldGenerator.instance.GetHeight(p.x, p.z) - TestTerrain.Height(p));
                }
            return worst;
        }

        internal static void Say(string text, Container chest)
        {
            TestRunner.Log($"> {text}");
            ChatNet.SendAsLocal(text, chest != null ? chest.GetComponent<ZNetView>().GetZDO().m_uid : ZDOID.None);
        }

        internal static int Count(Container c, string prefab)
        {
            int n = 0;
            foreach (var it in c.GetInventory().GetAllItems())
                if (it.m_dropPrefab != null && it.m_dropPrefab.name == prefab) n += it.m_stack;
            return n;
        }

        /// <summary>
        /// 거점 둘레의 지난 시험 상자를 없앤다(시험 월드만). 상자를 지정하지 않으니 남은 상자의 장비·음식·빈자리가 시험을 흔든다. 없앤 개수.
        /// </summary>
        internal static int ClearOldChests(Vector3 home)
        {
            int n = 0;
            foreach (var c in Chests.NearBase(home))
            {
                ZNetView nv = c.GetComponent<ZNetView>();
                if (nv == null || !nv.IsValid()) continue;
                nv.ClaimOwnership();
                ZNetScene.instance.Destroy(c.gameObject);
                n++;
            }
            if (n > 0) TestRunner.Log($"지난 시험 상자 {n}개 치움");
            return n;
        }

        /// <summary>거점 상자 전부(깃발 20m 안)에 든 그 물건 수. 상자는 지정하지 않으니 어느 상자에 들어갔는지는 안 본다.</summary>
        internal static int CountBase(Vector3 home, string prefab)
        {
            int n = 0;
            foreach (var c in Chests.NearBase(home)) n += Count(c, prefab);
            return n;
        }

        static readonly Vector3[] SiteSpots = { Vector3.zero, new Vector3(-4f, 0f, -1f), new Vector3(4f, 0f, -1f), new Vector3(0f, 0f, 12f) }; // 깃발·상자 2개·광맥(깃발 기준)

        /// <summary>
        /// 플레이어가 p에 서면 깃발·상자·광맥 자리가 모두 물 위 마른 땅인지. real이면 불러온 실제 땅, 아니면 WorldGenerator 높이(실제와 0.8m까지 다름 → 2m 여유)에 초원만.
        /// </summary>
        static bool DrySite(Vector3 p, Quaternion rot, bool real)
        {
            float wl = ZoneSystem.instance.m_waterLevel;
            Vector3 flag = p + rot * Vector3.forward * 4f;
            if (!real && WorldGenerator.instance.GetBiome(flag) != Heightmap.Biome.Meadows) return false;
            foreach (var o in SiteSpots)
            {
                Vector3 s = flag + rot * o;
                float h;
                if (real) { if (!ZoneSystem.instance.GetGroundHeight(s, out h)) return false; }
                else h = WorldGenerator.instance.GetHeight(s.x, s.z);
                if (h < wl + (real ? 1f : 2f)) return false;
            }
            return true;
        }

        /// <summary>플레이어 둘레 300m 안에서 DrySite인 가장 가까운 자리(땅 높이 포함), 없으면 from 그대로.</summary>
        static Vector3 FindDrySite(Vector3 from, Quaternion rot)
        {
            for (float r = 10f; r <= 300f; r += 10f)
                for (int d = 0; d < 16; d++)
                {
                    float a = d * Mathf.PI / 8f;
                    Vector3 p = from + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                    if (DrySite(p, rot, real: false)) return new Vector3(p.x, WorldGenerator.instance.GetHeight(p.x, p.z), p.z);
                }
            return from;
        }

        internal static Vector3 Ground(Vector3 p)
        {
            if (ZoneSystem.instance.GetGroundHeight(p, out float h)) p.y = h; // 지형(바위 위에 상자를 놓지 않게)
            return p;
        }

        internal static Container ChestAt(Vector3 pos)
        {
            foreach (var zdo in Senses.Nearby(pos, 1.5f))
            {
                ZNetView v = ZNetScene.instance.FindInstance(zdo);
                Container c = v != null ? v.GetComponent<Container>() : null;
                if (c != null) return c;
            }
            return null;
        }

        static void End(Player player, bool god, Checks checks, System.Action<string, string> onHeard, List<string> heard)
        {
            ChatNet.Heard -= onHeard;
            player.SetGodMode(god);
            TestRunner.Log("--- NPC 대사 ---");
            foreach (var h in heard) TestRunner.Log(h);
            TestRunner.Log($"판정: {(checks.Passed == checks.Total ? "PASS" : "FAIL")} ({checks.Passed}/{checks.Total})");
            TestRunner.Finish();
        }
    }
}
