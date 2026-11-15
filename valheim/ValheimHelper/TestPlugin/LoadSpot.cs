using System;
using System.Collections;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 모드 부하 재기 자리(사용자 11-15 "부하 재기 끝내는 조건": 메인 집 복사본·숲 필드 하나): VH_LOAD_AT=house|forest.
    /// 모드 끔 판(Bare)과 켬 판(vh_loadtest)이 같은 자리·같은 방향·같은 낮·맑음에서 재게 둘 다 이것으로 옮긴다(게임 함수만, 패치 없음).
    /// house = 기본 시험 월드 te4st41의 TheNewEra 복사본 집(HouseCopy.Place) 동쪽 38m에서 집을 봄, forest = 월드 가운데서 가까운 검은숲 마른 평지.
    /// </summary>
    static class LoadSpot
    {
        public static readonly string At = (Environment.GetEnvironmentVariable("VH_LOAD_AT") ?? "").Trim().ToLowerInvariant();

        public static bool Find(out Vector3 pos, out Quaternion rot, out string why)
        {
            pos = default; rot = Quaternion.identity; why = "";
            if (At == "house")
            {
                Vector3? c = HouseCopy.Place(out why);
                if (c == null) return false;
                pos = c.Value + new Vector3(HouseCopy.Radius + 8f, 0f, 0f);
                rot = Quaternion.LookRotation(Vector3.left);
            }
            else if (At == "forest")
            {
                Vector3? p = TestKit.FindBiomeSpot(Vector3.zero, Heightmap.Biome.BlackForest);
                if (p == null) { why = "검은숲 마른 평지 못 찾음"; return false; }
                pos = p.Value;
            }
            else { why = $"모르는 자리 {At}"; return false; }
            pos.y = WorldGenerator.instance.GetHeight(pos.x, pos.z) + 1f;
            why = $"{At} ({pos.x:0},{pos.z:0})";
            return true;
        }

        /// <summary>낮·맑음·약한 바람 고정(TestKit.Setup과 같은 값) — 끔 판도 같은 하늘에서 재게.</summary>
        public static void FixSky()
        {
            var env = EnvMan.instance;
            if (env == null) return;
            env.m_debugTimeOfDay = true;
            env.m_debugTime = 0.5f;
            env.m_debugEnv = "Clear";
            env.SetDebugWind(0f, 0.3f);
        }

        /// <summary>플레이어를 그 자리로 옮기고 다 옮겨질 때까지(최대 60초) 기다린다. 신 모드(끔 판도 몬스터에 안 죽게).</summary>
        public static IEnumerator Go(Vector3 pos, Quaternion rot)
        {
            Player pl = Player.m_localPlayer;
            pl.SetGodMode(true);
            for (float t = 0f; t < 10f && !pl.TeleportTo(pos, rot, true); t += 0.5f) yield return new WaitForSecondsRealtime(0.5f);
            yield return new WaitForSecondsRealtime(3f);
            for (float t = 0f; t < 60f && pl.IsTeleporting(); t += 0.5f) yield return new WaitForSecondsRealtime(0.5f);
            FixSky();
        }
    }
}
