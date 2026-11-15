using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
using ValheimHelper.Core.Npc;

namespace ValheimHelper
{
    /// <summary>
    /// 통째로 바꾼 게임 함수 변경 감지(사용자 11-15): 원본을 건너뛰고 원본과 같은 순서를 모드가 대신 하는 Prefix 목록과
    /// 그 원본 IL 해시를 모드 폴더 game-hashes.txt와 비교한다. 다르면 로그 경고 — 게임 업데이트로 원본이 바뀌었으니 디컴파일해 모드 쪽을 맞춘다.
    /// 해시는 원본 메타데이터 IL이라 Harmony 패치 뒤에도 그대로다. 파일이 없으면 지금 값으로 만든다(첫 실행).
    /// 조건만 걸어 막는 Prefix(return false만, 원본을 흉내 내지 않음 — SharedChests·SharedGuest 막기·RefillPick·NoDig 등)는 넣지 않는다.
    /// </summary>
    static class GameCheck
    {
        internal const string FileName = "game-hashes.txt";

        // 이름 → 원본 함수. 새로 원본을 대신하는 Prefix를 만들면 여기에 더한다(codemap "모드 두 개" 옆 줄).
        sealed class Target { internal string Name; internal Func<System.Reflection.MethodBase> Find; internal Target(string n, Func<System.Reflection.MethodBase> f) { Name = n; Find = f; } }

        static readonly Target[] Targets =
        {
            new Target("ZNetScene.CreateDestroyObjects", () => AccessTools.Method(typeof(ZNetScene), "CreateDestroyObjects")),   // FarZones: 늘 대신(중복 거르기·추적 물체 주변)
            new Target("ZDOMan.ReleaseNearbyZDOS", () => AccessTools.Method(typeof(ZDOMan), "ReleaseNearbyZDOS")),               // FarZones: 추적 물체 있을 때 호스트 몫
            new Target("Pickable.RPC_Pick", () => AccessTools.Method(typeof(Pickable), "RPC_Pick")),                             // ServerPick: 로컬 플레이어 없는 서버
            new Target("InventoryGui.UpdateContainer", () => AccessTools.Method(typeof(InventoryGui), "UpdateContainer")),       // SharedGuest: 손님이 보는 상자
            new Target("Player.ConsumeResources", () => AccessTools.Method(typeof(Player), nameof(Player.ConsumeResources),
                new[] { typeof(Piece.Requirement[]), typeof(int), typeof(int), typeof(int) })),                       // SharedCraft.BuildConsume: 손님 조각 재료
        };

        /// <summary>지금 게임의 해시(못 찾으면 "").</summary>
        internal static Dictionary<string, string> Now()
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var t in Targets)
            {
                string h = "";
                try { h = GameHashes.Hash(t.Find()?.GetMethodBody()?.GetILAsByteArray()); }
                catch (Exception e) { Plugin.Log.LogWarning($"[게임 함수] {t.Name} 못 읽음: {e.GetType().Name} {e.Message}"); }
                d[t.Name] = h;
            }
            return d;
        }

        /// <summary>시작 때 한 번. 바뀐 함수 수를 돌려준다(시험이 읽음).</summary>
        internal static int Run()
        {
            string path = Path.Combine(Plugin.Dir, FileName);
            var now = Now();
            string header = $"원본을 대신하는 Prefix의 게임 함수 IL 해시 — 게임 {Version.GetVersionString()} {DateTime.Now:yyyy-MM-dd}";
            if (!File.Exists(path))
            {
                File.WriteAllLines(path, GameHashes.Format(now, header));
                Plugin.Log.LogInfo($"[게임 함수] 기록 만듦 {now.Count}개 → {FileName}");
                return 0;
            }
            var known = GameHashes.Parse(File.ReadAllLines(path));
            int changed = 0, added = 0, missing = 0;
            foreach (var r in GameHashes.Compare(known, now))
            {
                if (r.State == GameHashes.State.Changed)
                {
                    changed++;
                    Plugin.Log.LogWarning($"[게임 함수 바뀜] {r.Name} 해시 {(r.Now == "" ? "(함수 못 찾음)" : r.Now)} ≠ 기록 {r.Known} — 모드가 이 원본을 대신하니 디컴파일해 맞출 것");
                }
                else if (r.State == GameHashes.State.New) { added++; known[r.Name] = r.Now; }
                else if (r.State == GameHashes.State.Missing) missing++;
            }
            // 새로 목록에 든 함수만 기록에 더한다. 바뀐 것은 고칠 때까지 옛 값을 둬서 매 시작 경고가 남게 한다.
            if (added > 0) File.WriteAllLines(path, GameHashes.Format(known, header));
            Plugin.Log.LogInfo($"[게임 함수] {now.Count}개 비교: 바뀜 {changed} · 새로 기록 {added} · 목록에서 빠짐 {missing}");
            return changed;
        }
    }
}
