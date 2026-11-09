using UnityEngine;
using ValheimHelper.Core.Npc;

namespace ValheimHelper
{
    /// <summary>
    /// 음식 효과가 끝날 때까지 줄지 않는 모드(사용자 11-09)를 이 PC 플레이어 상태로 알아채 일꾼에게도 같게(Meals.NoDecay).
    /// 5초마다: 바닐라라면 줄었을 음식이 있는데 최대 체력이 온전한 합이면 켬(모드가 m_health를 고치든 합을 고치든 잡힘). 한 번 켜지면 이 판 동안 유지.
    /// 바닐라 식은 Player.UpdateFood(남은 시간/m_foodBurnTime)^0.3, 합은 GetTotalFoodValue = m_baseHP + m_health 합(디컴파일 확인).
    /// 데디케이티드 서버(이 PC에 플레이어 없음)에서는 못 알아챔.
    /// </summary>
    static class FoodModWatch
    {
        static float s_timer;

        public static void Tick(float dt)
        {
            if (Meals.NoDecay) return;
            s_timer -= dt;
            if (s_timer > 0f) return;
            s_timer = 5f;
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
            if (!Meals.LooksNoDecay(p.GetBaseFoodHP(), p.GetMaxHealth(), food, time, burn)) return;
            Meals.NoDecay = true;
            Plugin.Log.LogInfo($"음식 효과 안 줄어드는 모드 알아챔(최대 체력 {p.GetMaxHealth():0}) → 일꾼 음식도 끝날 때까지 온전한 효과");
        }
    }
}
