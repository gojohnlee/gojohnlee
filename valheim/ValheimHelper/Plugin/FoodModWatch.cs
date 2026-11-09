using System;
using System.IO;
using UnityEngine;
using ValheimHelper.Core.Npc;

namespace ValheimHelper
{
    /// <summary>
    /// 음식 효과가 끝날 때까지 줄지 않는 모드(사용자 11-09)를 이 PC 플레이어 상태로 알아채 일꾼에게도 같게(Meals.NoDecay).
    /// 한 판에 한 번만 판정: 먹은 음식이 바닐라라면 뚜렷이 줄었을 만큼 지났을 때 최대 체력을 보고 있음·없음을 정하면 그 판엔 더 안 봄.
    /// 그 전까지는 지난 판 결과(플러그인 폴더 food-mod.txt)를 씀 — 게임을 켜자마자 맞게.
    /// 바닐라 식은 Player.UpdateFood(남은 시간/m_foodBurnTime)^0.3, 합은 GetTotalFoodValue = m_baseHP + m_health 합(디컴파일 확인).
    /// 데디케이티드 서버(이 PC에 플레이어 없음)에서는 판정 못 함(지난 결과만).
    /// </summary>
    static class FoodModWatch
    {
        static bool s_loaded, s_decided;
        static float s_timer;
        static string FilePath => Path.Combine(TestRunner.PluginDir, "food-mod.txt");

        public static void Tick(float dt)
        {
            if (!s_loaded)
            {
                s_loaded = true;
                try { if (File.Exists(FilePath)) Meals.NoDecay = File.ReadAllText(FilePath).Trim() == "1"; }
                catch (Exception e) { Plugin.Log.LogWarning("food-mod.txt 읽기 실패: " + e.Message); }
            }
            if (s_decided) return;
            // 먹은 뒤 절반쯤 지나야 판정되니 자주 볼 필요 없음(한 번 비교는 숫자 몇 개라 가벼움)
            s_timer -= dt;
            if (s_timer > 0f) return;
            s_timer = 30f;
            Player p = Player.m_localPlayer;
            if (p == null || p.IsDead()) return;
            var foods = p.GetFoods();
            if (foods == null || foods.Count == 0) return;
            float[] food = new float[foods.Count], time = new float[foods.Count], burn = new float[foods.Count];
            for (int i = 0; i < foods.Count; i++)
            {
                food[i] = foods[i].m_item.m_shared.m_food;
                time[i] = foods[i].m_time;
                burn[i] = foods[i].m_item.m_shared.m_foodBurnTime;
            }
            bool? mod = Meals.DecayVerdict(p.GetBaseFoodHP(), p.GetMaxHealth(), food, time, burn);
            if (mod == null) return;
            s_decided = true;
            Meals.NoDecay = mod.Value;
            Plugin.Log.LogInfo($"음식 모드 판정: {(mod.Value ? "있음 → 일꾼 음식도 끝날 때까지 온전한 효과" : "없음 → 바닐라대로")} (최대 체력 {p.GetMaxHealth():0})");
            try { File.WriteAllText(FilePath, mod.Value ? "1" : "0"); }
            catch (Exception e) { Plugin.Log.LogWarning("food-mod.txt 저장 실패: " + e.Message); }
        }
    }
}
