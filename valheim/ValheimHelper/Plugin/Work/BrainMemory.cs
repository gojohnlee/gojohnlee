using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Storage;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    public static partial class Brain
    {
        // ---- 일꾼 기억(지식·장소·적)은 서버 월드별 파일에, 월드 저장 순간에만 (사용자 11-11) ----
        // 지식은 본 구역마다 늘어 커진다 → 일꾼 ZDO에 두면 손님에게 통째로 계속 보내져 다른 ZDO가 막힘(E052, 회차 309).
        // 파일은 월드 폴더 곁(worlds_local/<월드>.vh-memory): 폴더 안은 게임이 이름 바꿀 때 모르는 파일을 지움(SaveSystem.RenameDirectory),
        // 곁의 모르는 확장자는 월드 목록이 안 봄(SaveSystem.IsWorldSaveExtension). 게임 월드 백업에는 안 들어감.
        /// <summary>집 안 길 기억(요청 11-15, 기억 파일 판 2). 건축 바뀜·나이로 지우기는 BrainPaths.cs, 길 찾기 연결은 아직.</summary>
        public static PathMemory Paths { get { Get(); return s_paths; } }
        static readonly int[] MemoryKeys = { Key.GetStableHashCode(), KeyPlaces.GetStableHashCode(), KeyFoes.GetStableHashCode() };
        /// <summary>월드 불러올 때 읽은 지식 구역 수(시험용).</summary>
        public static int LoadedZones;

        /// <summary>서버 월드의 기억 파일 경로. 월드를 모르면 null.</summary>
        static string MemoryPath()
        {
            var w = ZNet.GetWorldIfIsHost();
            if (w == null || string.IsNullOrEmpty(w.m_worldName)) return null;
            string root = w.m_fileSource.IsLocal() ? SaveSystem.GetWorldsSaveRootPath(w.m_fileSource)
                : Path.Combine(BepInEx.Paths.ConfigPath, "ValheimHelper", "worlds"); // 클라우드 월드는 파일 경로가 없음
            return Path.Combine(root, w.m_worldName + ".vh-memory");
        }

        /// <summary>파일에서 지식·장소·적을 읽는다. 없거나 못 읽으면 false(그땐 옛 ZDO 것).</summary>
        static bool ReadMemory(string path, ref string knowledge, ref string places, ref string foes, ref string paths)
        {
            if (path == null || !File.Exists(path)) return false;
            try
            {
                using (var s = File.OpenRead(path))
                {
                    var m = MemoryFile.Read(s); // 판 1(길 없음)도 읽음
                    knowledge = m.Knowledge; places = m.Places; foes = m.Foes; paths = m.Paths;
                }
                return true;
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"일꾼 기억 파일 못 읽음({path}): {e.Message} — ZDO 것으로"); return false; }
        }

        /// <summary>불러온 뒤(Get): 서버에선 일꾼 ZDO의 옛 기억 키를 뺀다(손님에게 안 보내게, 다음 저장부터 ZDO엔 없음).</summary>
        static void StripMemory(List<ZDO> zdos)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            foreach (var zdo in zdos)
                foreach (int h in MemoryKeys) zdo.RemoveString(h);
        }

        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.PrepareSave))]
        static class SaveMemory
        {
            static readonly FieldInfo s_endSave = AccessTools.Field(typeof(ZDOMan), "m_endSave");
            static readonly MethodInfo s_updateSaveState = AccessTools.Method(typeof(ZDOMan), "UpdateSaveState");

            static void Prefix()
            {
                // E072: 앞 저장이 끝났어도 "저장 중" 표(둘째 표에 바뀐 조각)는 다음 ZDOMan.Update에서야 풀림 → 그 전에 다시 저장(끄기)하면
                // 이번 저장이 첫째 표만 보고 바뀐 조각을 안 씀. 게임이 다음 프레임에 할 합치기를 지금 함(SaveWorld가 앞 저장 스레드를 기다린 뒤라 안전).
                try
                {
                    if (s_endSave?.GetValue(ZDOMan.instance) != null) s_updateSaveState?.Invoke(ZDOMan.instance, null);
                }
                catch (System.Exception e) { Plugin.Log.LogWarning("E072 저장 전 확인 못 함: " + e.Message); }
                if (s_k != null && s_for != ZDOMan.instance) Plugin.Log.LogInfo("일꾼 기억 저장 건너뜀: ZDOMan 바뀜");
                if (s_k == null || s_for != ZDOMan.instance || s_file == null || ZNet.instance == null || !ZNet.instance.IsServer()) return; // 안 불러왔으면 파일·옛 ZDO 그대로
                int pruned = s_paths?.Prune(ZNet.instance.GetTimeSeconds(), PathMaxAge) ?? 0;
                if (pruned > 0) Plugin.Log.LogInfo($"길 기억 오래된 것 지움: {pruned}개");
                var m = new MemoryFile { Knowledge = s_k.Save(), Places = s_places.Save(), Foes = s_foes.Save(), Paths = s_paths?.Save() ?? "" };
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(s_file));
                    string tmp = s_file + ".new";
                    using (var s = File.Create(tmp)) m.Write(s);
                    if (File.Exists(s_file)) File.Replace(tmp, s_file, null);
                    else File.Move(tmp, s_file);
                    Plugin.Log.LogInfo($"일꾼 기억 파일 저장: {Path.GetFileName(s_file)} {new FileInfo(s_file).Length}바이트, 지식 {s_k.Count}구역({m.Knowledge.Length}자), 장소 {m.Places.Length}자, 적 {m.Foes.Length}자, 길 {s_paths?.Count ?? 0}개({m.Paths.Length}자)");
                }
                catch (System.Exception e) { Plugin.Log.LogWarning($"일꾼 기억 파일 저장 못 함({s_file}): {e.Message}"); }
            }
        }
    }
}
