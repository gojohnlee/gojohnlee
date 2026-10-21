using System.Collections;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_skilltest: 일꾼 스킬 성장(SkillBook). 도끼 스킬을 올리면 레벨·피해 계수가 오르고,
    /// ZDO(vh_skills)에 저장되어 다시 불러와도 같으며, 다른 일꾼에게는 영향이 없는지.
    /// </summary>
    static class SkillTest
    {
        public static void Register() => TestRunner.Register("vh_skilltest", "스킬 성장·저장 시험", () => TestKit.Flat(Run()));

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

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks)
        {
            yield return TestKit.Setup(w);
            checks.Add("NPC 둘", w.Claude != null && w.Alarak != null);
            if (w.Claude == null || w.Alarak == null) yield break;
            var skill = Skills.SkillType.Axes;
            int id = (int)skill;
            float before = w.Claude.SkillLevels.Level(id);
            float otherBefore = w.Alarak.SkillLevels.Level(id);
            float factorBefore = w.Claude.GetSkillFactor(skill);
            for (int i = 0; i < 400; i++) w.Claude.RaiseSkill(skill, 1f);
            yield return null;
            float after = w.Claude.SkillLevels.Level(id);
            checks.Add($"도끼 레벨 오름 ({before:0.0} → {after:0.0})", after > before);
            checks.Add($"70 넘지 않음 ({after:0.0})", after <= 70.001f);
            checks.Add("피해 계수 오름", w.Claude.GetSkillFactor(skill) > factorBefore);
            checks.Add($"다른 일꾼은 그대로 ({otherBefore:0.0} → {w.Alarak.SkillLevels.Level(id):0.0})", Mathf.Approximately(w.Alarak.SkillLevels.Level(id), otherBefore));

            string saved = w.Claude.GetComponent<ZNetView>().GetZDO().GetString(WorkerNpc.KeySkills, "");
            checks.Add("ZDO에 저장됨", saved.Length > 0);
            var loaded = Core.Npc.SkillBook.Load(saved);
            checks.Add($"불러와도 같음 ({loaded.Level(id):0.0})", Mathf.Abs(loaded.Level(id) - after) < 0.01f);
        }
    }
}
