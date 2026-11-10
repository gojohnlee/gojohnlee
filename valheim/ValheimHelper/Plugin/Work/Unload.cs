using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 짐 정리: 들고 다닐 것(Prep.Kept) 말고는 거점 상자에 넣는다(장비는 장비 상자, 나머지는 자원 상자). 상자마다 걸어가서 한 번에.
    /// 넣을 상자가 없거나 자리가 없는 물건은 건너뛴다. 묘비 회수 뒤(GraveJob), 채집 중 짐을 넣을 때(GatherJob: 다른 짐으로 가방이 참).
    /// </summary>
    public sealed class Unload
    {
        const float Limit = 90f, BusyLimit = 60f;

        readonly WorkerNpc m_npc;
        readonly WorkerAI m_ai;
        DoorWay m_way; Container m_wayFor;
        /// <summary>이 물건은 넣지 않는다(채집 중인 물건: 그건 GatherJob이 세면서 넣는다).</summary>
        readonly string m_except;
        readonly HashSet<ItemDrop.ItemData> m_skip = new HashSet<ItemDrop.ItemData>();
        /// <summary>이 프리팹들도 넣지 않는다(GatherJob: 일터로 들고 가는 포탈 한 몫 — 불러온 뒤 첫 짐 정리에서 상자로 가지 않게).</summary>
        public HashSet<string> Hold;
        /// <summary>이 프리팹들은 들고 다닐 음식이어도 다 넣는다(일의 결과물 — 밭에서 거둔 작물·설비에서 만든 음식. 회차 380: 음식 3개 들고 다니기가 거둔 당근·구운 고기를 붙잡음).</summary>
        public System.Predicate<string> Deliver;
        bool Delivers(ItemDrop.ItemData it) => Deliver != null && it.m_dropPrefab != null && Deliver(it.m_dropPrefab.name);
        /// <summary>걷지 않고 바로 넣는다(밤사이 정산 — 보는 사람이 없을 때만).</summary>
        public bool Instant;
        /// <summary>넣을 상자가 꽉 차(또는 없어) 못 넣고 건너뛴 것.</summary>
        public IEnumerable<ItemDrop.ItemData> Skipped => m_skip;
        Container m_chest;
        float m_time;
        /// <summary>
        /// 손 거리 넣기(사용자 10-31 ③): 상자 Reach m 안에서 0.5초(StillSeconds) 더 못 다가가면(통나무·동료·설비에 막힘) 사이를 건축물이 안 가리고
        /// 높이 차가 작을 때 그 자리에서 넣는다(플레이어 손 거리 Player.m_maxInteractDistance 5 — 눈 높이 차를 빼고 4m).
        /// </summary>
        const float Reach = 4f, StillSeconds = 0.5f;
        float m_best = float.MaxValue, m_still;
        readonly ChestHop m_hop = new ChestHop();
        /// <summary>시험용: 손 거리 넣기 끔(고치기 전 비교)·한 수.</summary>
        internal static bool TestNoReach;
        internal static int Reached;

        public Unload(WorkerNpc npc, string except = null)
        {
            m_npc = npc;
            m_ai = npc.Ai;
            m_except = except;
        }

        /// <summary>넣을 게 있는지(상자가 있는지는 안 본다).</summary>
        public static bool Needed(WorkerNpc npc, string except = null) => new Unload(npc, except).NextToStore() != null;

        /// <returns>true = 넣는 중(걷거나 기다림), false = 끝.</returns>
        public bool Update(float dt)
        {
            m_time += dt;
            if (m_time > Limit) { Plugin.Log.LogInfo($"DIAG Unload 시간 초과 {m_time:0.0}"); return false; }
            if (m_chest == null)
            {
                ItemDrop.ItemData next = NextToStore();
                if (next == null) { Plugin.Log.LogInfo($"DIAG Unload 다 넣음/없음 ({m_time:0.0}초) 짐 [{BagDump()}] 지킴 {Prep.Kept(m_npc).Count} skip {m_skip.Count}"); return false; } // 다 넣었다
                m_chest = ChestFor(next);
                if (m_chest == null) { Plugin.Log.LogInfo($"DIAG Unload 넣을 상자 없음 {next.m_dropPrefab.name} (역할 {Chests.RoleFor(next)}, 상자 {Chests.NearBase(m_npc.Home, Chests.RoleFor(next)).Count})"); m_skip.Add(next); return true; }
            }
            var at = m_chest.transform.position;
            float d = Utils.DistanceXZ(at, m_npc.transform.position);
            if (d < m_best - 0.3f) { m_best = d; m_still = 0f; } else m_still += dt;
            if (!Instant && (d > 2.5f || Mathf.Abs(at.y - m_npc.transform.position.y) > 1.5f) && !(m_wayFor == m_chest && m_way.Busy) && !CanReach(at, d))
            {
                if (!m_hop.Update(dt, m_npc, m_chest, d, "Unload")) { bool floor = Mathf.Abs(at.y - m_npc.transform.position.y) > 1.5f; m_ai.MoveToPoint(dt, floor ? m_ai.FloorStep(m_hop.Goal(m_chest)) : m_hop.Goal(m_chest), floor ? 0f : 1.8f, d > 10f); } // 다른 층 상자면 길 모퉁이를 따라감(E063, Prep과 같음), 거점 125m: 멀면 뜀(Prep과 같음), 2.5초 거의 제자리면 옆으로, 5초면 상자 앞으로
                return true;
            }
            if (m_wayFor != m_chest) { m_way = new DoorWay(); m_wayFor = m_chest; }
            if (!Instant && m_way.Steer(dt, m_npc, m_chest)) return true; // 집 안 상자: 벽이 가리면 문으로(회차 311)
            Container chest = m_chest;
            HashSet<ItemDrop.ItemData> keep = Prep.Kept(m_npc);
            Inventory mine = m_npc.GetInventory();
            int bagBefore = Stacks(mine);
            bool done = Chests.TryEdit(chest, inv =>
            {
                foreach (var it in new List<ItemDrop.ItemData>(mine.GetAllItems()))
                {
                    if (!Storable(it, keep) || ChestFor(it) != chest || !inv.CanAddItem(it)) continue;
                    if (keep.Contains(it) && !Delivers(it)) // 들고 다닐 미드·음식 더미: 몫을 넘는 만큼만
                    {
                        int extra = Prep.ExtraMeads(m_npc, it);
                        ItemDrop.ItemData part = it.Clone();
                        part.m_stack = extra;
                        // 바닐라 AddItem은 기존 더미에 일부만 채우고 false를 낼 수 있음(남은 수 = part.m_stack) → 들어간 만큼만 뺀다
                        int moved = extra <= 0 ? 0 : inv.AddItem(part) ? extra : extra - part.m_stack;
                        if (moved > 0) mine.RemoveItem(it, moved);
                        continue;
                    }
                    if (m_npc.IsWearing(it)) m_npc.UnequipItem(it, false);
                    inv.MoveItemToThis(mine, it);
                }
            });
            if (!done && m_time < BusyLimit) // 누가 열고 있다
            {
                // 다른 상자에 넣을 수 있으면 그리로(헛기다림 ①, 사용자 11-11), 1초마다 봄
                if ((m_recheck += dt) >= 1f) { m_recheck = 0f; if (OtherChest(chest, keep)) { Plugin.Log.LogInfo($"DIAG Unload 열린 상자 대신 다른 상자로 ({m_time:0.0}초)"); m_chest = null; m_best = float.MaxValue; m_still = 0f; } }
                return true;
            }
            int put = bagBefore - Stacks(mine);
            // 어느 상자에 넣었나(10-29 vh_bagtest: 깃발 125m 가장자리 옛 상자에 넣은 나무를 시험이 못 세 "사라짐"으로 보임)
            if (put > 0) Plugin.Log.LogInfo($"DIAG Unload 넣음 {put}개 → 상자 {chest.GetComponent<ZNetView>()?.GetZDO()?.m_uid} 집에서 {Utils.DistanceXZ(chest.transform.position, m_npc.Home):0.0}m");
            // 이 상자에 못 넣은 건 건너뛴다(자리가 없음)
            foreach (var it in mine.GetAllItems()) if (Storable(it, keep) && ChestFor(it) == chest) { m_skip.Add(it); Plugin.Log.LogInfo($"DIAG Unload 못 넣고 건너뜀 {it.m_dropPrefab.name}x{it.m_stack} (편집 {(done ? "됨" : "못 함")}, 상자 빈칸 {chest.GetInventory().GetEmptySlots()})"); }
            m_chest = null;
            m_best = float.MaxValue; m_still = 0f;
            return true;
        }

        float m_recheck;

        bool OtherChest(Container busy, HashSet<ItemDrop.ItemData> keep)
        {
            if (!Chests.InUse(busy)) return false;
            foreach (var it in m_npc.GetInventory().GetAllItems())
            {
                if (!Storable(it, keep)) continue;
                Container c = ChestFor(it);
                if (c != null && c != busy && !Chests.InUse(c)) return true;
            }
            return false;
        }

        bool CanReach(Vector3 at, float d)
        {
            if (TestNoReach || d > Reach || m_still < StillSeconds || Mathf.Abs(at.y - m_npc.transform.position.y) > 1f || DoorWay.Blocked(m_npc, m_chest)) return false;
            Reached++;
            Plugin.Log.LogInfo($"DIAG {m_npc.NpcName} Unload 손 거리 넣기: {m_still:0.0}초 못 다가감, 상자까지 {d:0.0}m / {m_ai.BlockerAhead(at)}");
            return true;
        }

        /// <summary>시험용(E055): 손 거리 넣기를 건너뛴 까닭.</summary>
        internal string Why()
        {
            if (m_chest == null) return "상자 안 고름";
            var at = m_chest.transform.position;
            return $"상자까지 {Utils.DistanceXZ(at, m_npc.transform.position):0.0}m(최소 {m_best:0.0}) 높이 차 {at.y - m_npc.transform.position.y:0.0} 제자리 {m_still:0.0}초 문막힘 {DoorWay.Blocked(m_npc, m_chest)} 문길 {(m_wayFor == m_chest && m_way.Busy)} 목표 {m_hop.Goal(m_chest)} / {m_hop.State(m_npc, m_chest)}";
        }

        static int Stacks(Inventory inv)
        {
            int n = 0;
            foreach (var it in inv.GetAllItems()) n += it.m_stack;
            return n;
        }

        string BagDump()
        {
            var parts = new List<string>();
            foreach (var it in m_npc.GetInventory().GetAllItems())
                parts.Add((it.m_dropPrefab != null ? it.m_dropPrefab.name : "null-prefab:" + it.m_shared.m_name) + "x" + it.m_stack);
            return string.Join(", ", parts);
        }

        Container ChestFor(ItemDrop.ItemData it) => Chests.ForItem(it.m_dropPrefab.name, m_npc.Home, Chests.RoleFor(it), true);

        // 거점에서 고칠 수 있는 닳은 장비는 지닌다(다음 출발 준비가 고침, 상자에 넣으면 부러진 채 남던 것)
        bool Storable(ItemDrop.ItemData it, HashSet<ItemDrop.ItemData> keep) =>
            it.m_dropPrefab != null && (!keep.Contains(it) || Delivers(it) || Prep.ExtraMeads(m_npc, it) > 0) && !m_skip.Contains(it) && it.m_dropPrefab.name != m_except
            && (Hold == null || !Hold.Contains(it.m_dropPrefab.name))
            && Repair.StationFor(it, m_npc.Home) == null;

        ItemDrop.ItemData NextToStore()
        {
            HashSet<ItemDrop.ItemData> keep = Prep.Kept(m_npc);
            foreach (var it in m_npc.GetInventory().GetAllItems())
                if (Storable(it, keep)) return it;
            return null;
        }
    }

    /// <summary>
    /// 상자 가기 막힘(사용자 10-31): 상자에 가까워지지 않고 거의 제자리(1.5m 안)로 2.5초면 상자 옆 접근점(StuckHop.Side 90°, 다음엔 -90°)으로 돌아 보고
    /// (MachineStep.Approach와 같음), NearStuckSeconds(5초)면 상자 앞으로 옮긴다(GatherJob 넣기와 같음). 굽은 길을 돌아 걷는 중은 아님.
    /// 사이를 건축물이 가리면(집 안 상자, DoorWay.Blocked) 벽 너머라 돌지도 옮기지도 않는다. Unload·Prep이 씀.
    /// 원인이 보이면 0.5초에(사용자 10-31): 앞 1m가 움직이는 Rigidbody(통나무 등)면 바로 옆으로, 닫힌 문이면 바로 연다(WorkerAI.BumpAhead).
    /// </summary>
    internal sealed class ChestHop
    {
        const float SideSeconds = 2.5f, SideRadius = 1.5f, BumpSeconds = 0.5f, FarStep = 1f, SlowSpeed = 1.5f;
        Container m_for;
        Vector3 m_from;
        Vector3? m_side;
        int m_sides;
        float m_best = float.MaxValue, m_time, m_far, m_farMark = float.MaxValue, m_age, m_farAt, m_slow, m_win; Vector3 m_winAt; int m_farResets; // E069 진단: 기준 갱신 횟수·마지막 시각
        Vector3 m_last;
        /// <summary>시험용: 옮긴 수, 옆으로 돈 수.</summary>
        internal static int Hops, Sides, Bumps, Doors;
        /// <summary>시험용: 닿은 것 보고 일찍 반응하기를 끔(고치기 전 비교).</summary>
        internal static bool TestNoBump;

        /// <summary>걸어갈 곳: 옆 접근점을 도는 중이면 거기, 아니면 상자.</summary>
        public Vector3 Goal(Container chest) => m_side ?? chest.transform.position;

        /// <returns>true = 이번에 옮김.</returns>
        public bool Update(float dt, WorkerNpc npc, Container chest, float d, string what)
        {
            Vector3 pos = npc.transform.position;
            if (m_for != chest) { m_for = chest; m_best = m_farMark = float.MaxValue; m_far = 0f; m_side = null; m_sides = 0; m_age = 0f; m_farResets = 0; m_slow = m_win = 0f; m_winAt = pos; }
            m_age += dt;
            if (m_side.HasValue && Utils.DistanceXZ(m_side.Value, pos) < 0.4f) m_side = null; // 옆 접근점에 닿음 → 거기서 곧장
            // 옆으로 돌거나 통나무를 밀며 느리게 가면 "거의 제자리" 시간이 0부터라, 1m 넘게 안 가까워진 시간도 따로 센다(E060) — 5초면 상자 앞으로.
            // 옆으로 돌아 본 뒤나 느리게(1.5m/s 밑) 움직일 때만 셈: 계단을 오를 때처럼 제 속도로 걷는데 수평 거리만 안 줄 때·상자가 1.5m 넘게 위(다른 층)일 때는 아님. 일꾼이 위면 셈(통나무 더미에 올라탐, E067)
            bool slow = dt > 0f && Utils.DistanceXZ(pos, m_last) < SlowSpeed * dt; m_last = pos;
            if (d < m_farMark - FarStep) { m_farMark = d; m_far = 0f; m_farResets++; m_farAt = m_age; } else if ((m_sides > 0 || slow) && chest.transform.position.y - pos.y < 1.5f) m_far += dt;
            // 상자 10m 안에서 0.5초씩 재어 느린(1.5m/s 밑) 시간을 합쳐 5초면 상자 앞으로: 통나무 사이·위로 1m씩 기어가면 위 두 셈이 거듭 0부터라 7~13초 걸렸음(E069).
            m_win += dt;
            if (m_win >= 0.5f) { if (d < 10f && Utils.DistanceXZ(pos, m_winAt) < SlowSpeed * m_win && chest.transform.position.y - pos.y < 1.5f) m_slow += m_win; m_win = 0f; m_winAt = pos; }
            if (m_slow >= (float)Core.Work.StuckHop.NearStuckSeconds && !TestNoBump && !DoorWay.Blocked(npc, chest)) return Hop(npc, chest, d, what, $"느린 시간 합 {m_slow:0.0}초");
            if (m_far >= (float)Core.Work.StuckHop.NearStuckSeconds && !DoorWay.Blocked(npc, chest)) return Hop(npc, chest, d, what, $"{m_far:0.0}초 안 가까워짐");
            if (d < m_best - 0.3f || Utils.DistanceXZ(m_from, pos) >= 1.5f) { m_best = Mathf.Min(m_best, d); m_from = pos; m_time = 0f; return false; }
            m_time += dt;
            Vector3 at = chest.transform.position;
            string bump = null;
            if (m_time >= BumpSeconds && !TestNoBump)
            {
                bump = npc.Ai.BumpAhead(Goal(chest), out Door door);
                if (door != null && Dungeons.Open(npc, door))
                {
                    Doors++; DoorWay.Track(door, npc);
                    Plugin.Log.LogInfo($"DIAG {npc.NpcName} {what} 상자 가기: {m_time:0.0}초 닫힌 문 {door.name}에 막힘 → 열기");
                    m_time = 0f;
                    return false;
                }
            }
            if ((m_time < SideSeconds && bump == null) || DoorWay.Blocked(npc, chest)) return false;
            if (m_time < (float)Core.Work.StuckHop.NearStuckSeconds)
            {
                if (m_side.HasValue || m_sides >= 2) return false;
                if (bump != null && m_time < SideSeconds) Bumps++;
                var p = Core.Work.StuckHop.Side(pos.x, pos.z, at.x, at.z, SideRadius, m_sides++);
                m_side = new Vector3((float)p.X, at.y, (float)p.Z);
                Sides++;
                Plugin.Log.LogInfo($"DIAG {npc.NpcName} {what} 상자 가기 막힘: {m_time:0.0}초 거의 제자리{(bump != null ? $", {bump}에 닿음" : "")}, 상자까지 {d:0.0}m / {npc.Ai.BlockerAhead(at)} → 옆({Core.Work.StuckHop.SideAngles[m_sides - 1]:0}°)으로");
                return false;
            }
            return Hop(npc, chest, d, what, $"{m_time:0.0}초 거의 제자리");
        }

        /// <summary>시험용(E067): 상자 앞 옮기기 셈 상태.</summary>
        internal string State(WorkerNpc npc, Container chest) => $"안 가까워짐 {m_far:0.0}초(기준 {m_farMark:0.0}m, 갱신 {m_farResets}번 마지막 {m_farAt:0.0}/{m_age:0.0}초) 느린 합 {m_slow:0.0}초 제자리 {m_time:0.0}초 옆 {m_sides}{(m_side.HasValue ? $"(남은 {Utils.DistanceXZ(m_side.Value, npc.transform.position):0.0}m)" : "")} 높이 차 {chest.transform.position.y - npc.transform.position.y:0.0}";

        bool Hop(WorkerNpc npc, Container chest, float d, string what, string why)
        {
            Plugin.Log.LogInfo($"DIAG {npc.NpcName} {what} 상자 가기 막힘: {why}, 상자까지 {d:0.0}m / {npc.Ai.BlockerAhead(Goal(chest))} → 상자 앞으로");
            npc.TeleportTo(chest.transform.position + chest.transform.forward * 1.5f, npc.transform.rotation, false);
            WorkerAI.StuckTeleports++;
            Hops++;
            m_time = 0f; m_far = 0f; m_farMark = float.MaxValue; m_best = float.MaxValue; m_side = null; m_slow = m_win = 0f; m_winAt = npc.transform.position;
            return true;
        }
    }
}
