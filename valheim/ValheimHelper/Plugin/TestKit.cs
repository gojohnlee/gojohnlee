using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 묶음 1 새 시험(vh_departtest·vh_peertest) 공통: 깃발·NPC·상자 준비, 조건 기다리기, 구역 내려감 흉내.
    /// 기존 시험 파일은 건드리지 않으려고 따로 둔다.
    /// </summary>
    static class TestKit
    {
        public sealed class World
        {
            public Player Player;
            public bool God;
            public EnvMan Env;
            public bool DebugTime;
            public float DebugValue;
            public Vector3 Home;
            public WorkerNpc Claude, Alarak;
            public Container Chest;
            public readonly List<string> Heard = new List<string>();
            public System.Action<string, string> OnHeard;
            public string Knowledge0, Places0;
        }

        /// <summary>WaitFor 결과(코루틴이라 out을 못 씀).</summary>
        public static bool Ok;
        public static float Waited;

        /// <summary>cond가 될 때까지 최대 max초. 결과는 Ok·Waited.</summary>
        public static IEnumerator WaitFor(System.Func<bool> cond, float max)
        {
            Waited = 0f;
            Ok = cond();
            while (!Ok && Waited < max)
            {
                yield return null;
                Waited += Time.deltaTime;
                Ok = cond();
            }
        }

        /// <summary>
        /// 낮 고정, 신 모드, NPC를 지우고 깃발을 세워 둘을 부르고, 거점 3m에 상자 하나(지정 안 함). 지식·장소 기억은 비워 둔다
        /// (끝나면 End가 원래대로). 실패하면 null(이유는 로그).
        /// </summary>
        public static IEnumerator Setup(World w, Vector3? around = null)
        {
            w.Player = Player.m_localPlayer;
            w.God = w.Player.InGodMode();
            w.Player.SetGodMode(true);
            w.Env = EnvMan.instance;
            w.DebugTime = w.Env.m_debugTimeOfDay;
            w.DebugValue = w.Env.m_debugTime;
            w.Env.m_debugTimeOfDay = true;
            w.Env.m_debugTime = 0.5f;
            w.OnHeard = (n, t) => w.Heard.Add(n + ": " + t);
            ChatNet.Heard += w.OnHeard;
            w.Knowledge0 = Brain.Get().Save();
            w.Places0 = Brain.Places.Save();

            // around가 있으면 그 자리 둘레(동시 시험용)만 정리하고 거기에 세운다. 없으면 예전처럼 전부 정리·플레이어 앞.
            if (around.HasValue) Workers.DespawnNear(around, 60f); else Workers.DespawnAll();
            yield return new WaitForSeconds(1f);
            Vector3 home = (around ?? w.Player.transform.position) + w.Player.transform.forward * 4f;
            if (ZoneSystem.instance.GetGroundHeight(home, out float gh)) home.y = gh;
            w.Home = home;
            // 지난 시험 상자(사용 중 표시가 남으면 비우지도 못해 재고가 틀어짐, 회차 60)를 치운다. 동시 시험(around)은 남의 상자라 안 건드림.
            if (!around.HasValue && GatherTest.ClearOldChests(home) > 0) yield return new WaitForSeconds(0.5f);
            w.Player.PlacePiece(BaseFlag.Prefab.GetComponent<Piece>(), home, w.Player.transform.rotation, doAttack: false);
            yield return new WaitForSeconds(3f);
            w.Claude = WorkerNpc.All.Find(n => n.Personality == Core.Npc.Personality.Scv);
            w.Alarak = WorkerNpc.All.Find(n => n.Personality == Core.Npc.Personality.Alarak);
            if (w.Claude == null || w.Alarak == null) yield break;
            foreach (var npc in new[] { w.Claude, w.Alarak })
            {
                foreach (var n in new[] { "AxeFlint", "PickaxeAntler", "ArmorLeatherChest", "ArmorLeatherLegs" })
                    npc.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab(n), 1);
                Prep.WearBest(npc);
                var food = new Inventory("vh_test_food", null, 4, 2);
                foreach (var n in new[] { "CookedMeat", "Honey", "Blueberries" }) food.AddItem(ObjectDB.instance.GetItemPrefab(n), 1);
                npc.EatFrom(food);
            }
            Vector3 right = Vector3.Cross(Vector3.up, w.Player.transform.forward).normalized;
            Vector3 spot = GatherTest.Ground(home + right * 3f);
            w.Player.PlacePiece(ZNetScene.instance.GetPrefab("piece_chest_wood").GetComponent<Piece>(), spot, w.Player.transform.rotation, doAttack: false);
            yield return new WaitForSeconds(1f);
            w.Chest = GatherTest.ChestAt(spot);
            Brain.ReplaceForTest("", "");
        }

        /// <summary>
        /// 중첩 코루틴(yield return IEnumerator)을 한 줄로 편다. Unity에 넘기면 안쪽 예외를 TestRunner.Safe가 못 잡아
        /// 시험이 FAIL 없이 멈추므로, 새 시험은 등록할 때 이걸 거친다.
        /// </summary>
        public static IEnumerator Flat(IEnumerator root)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                IEnumerator top = stack.Peek();
                if (!top.MoveNext()) { stack.Pop(); continue; }
                if (top.Current is IEnumerator inner) { stack.Push(inner); continue; }
                yield return top.Current;
            }
        }

        /// <summary>시험이 바꾼 것을 되돌린다(예외로 끝나도 finally에서 부름, 여러 번 불러도 됨).</summary>
        public static void Restore(World w)
        {
            if (w.OnHeard != null) { ChatNet.Heard -= w.OnHeard; w.OnHeard = null; }
            if (w.Player != null) w.Player.SetGodMode(w.God);
            if (w.Env != null)
            {
                w.Env.m_debugTimeOfDay = w.DebugTime;
                w.Env.m_debugTime = w.DebugValue;
            }
            if (w.Knowledge0 != null)
            {
                Brain.ReplaceForTest(w.Knowledge0, w.Places0); // 시험이 만든 지식·장소 기억을 지우고 원래대로
                Brain.Save();
                w.Knowledge0 = null;
            }
        }

        public static void End(World w, GatherTest.Checks checks)
        {
            Restore(w);
            bool clean = true;
            foreach (var h in w.Heard) if (h.Contains("{")) clean = false;
            checks.Add("대사에 자리표시자 안 남음", clean);
            TestRunner.Log("--- NPC 대사 ---");
            foreach (var h in w.Heard) TestRunner.Log(h);
            TestRunner.Log($"판정: {(checks.Passed == checks.Total ? "PASS" : "FAIL")} ({checks.Passed}/{checks.Total})");
            TestRunner.Finish();
        }

        public static void PutInChest(Container c, string prefab, int n) =>
            Chests.TryEdit(c, inv => inv.AddItem(ObjectDB.instance.GetItemPrefab(prefab), n));

        public static void TakeFromChest(Container c, string prefab)
        {
            string name = ObjectDB.instance.GetItemPrefab(prefab).GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
            Chests.TryEdit(c, inv => inv.RemoveItem(name, inv.CountItems(name)));
        }

        /// <summary>거점 상자 전부(지난 시험이 남긴 것 포함)에서 그 물건들을 비운다. 비운 개수.</summary>
        public static int ClearBaseStock(World w, params string[] prefabs)
        {
            int n = 0;
            foreach (var c in Chests.NearBase(w.Home))
                foreach (var p in prefabs)
                {
                    int had = GatherTest.Count(c, p);
                    if (had > 0) { TakeFromChest(c, p); n += had - GatherTest.Count(c, p); }
                }
            return n;
        }

        static readonly AccessTools.FieldRef<ZNetScene, Dictionary<ZDO, ZNetView>> Instances =
            AccessTools.FieldRefAccess<ZNetScene, Dictionary<ZDO, ZNetView>>("m_instances");

        /// <summary>
        /// 구역이 내려간 것처럼 인스턴스만 없앤다(ZDO는 남김) — 바닐라 ZNetScene.RemoveObjects와 같은 순서.
        /// 플레이어 근처면 ZNetScene이 다음 프레임에 ZDO에서 다시 만든다(재접속·구역 다시 불러오기 흉내).
        /// </summary>
        public static void Unload(ZNetView view)
        {
            if (view == null || !view.IsValid()) return;
            ZDO zdo = view.GetZDO();
            view.ResetZDO();
            Object.Destroy(view.gameObject);
            Instances(ZNetScene.instance).Remove(zdo);
        }

        /// <summary>NPC 인스턴스(이름으로, 다시 만들어진 것도).</summary>
        public static WorkerNpc Npc(Core.Npc.Personality p) => WorkerNpc.All.Find(n => n != null && n.Personality == p);

        public static string Name(WorkerNpc npc) => npc != null ? npc.NpcName : "?";
    }
}
