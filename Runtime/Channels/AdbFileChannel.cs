using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FunBrainLab.VRLink
{
    /// <summary>
    /// USB(ADB) 통로. 네이티브 플러그인 없이 앱 전용 폴더의 파일로 주고받습니다.
    ///   폴더: /sdcard/Android/data/&lt;패키지명&gt;/files/vrlink/
    ///   웹앱 → 앱: inbox/params.json, inbox/actions.json 을 adb push (앱이 읽고 지움 = 수신 확인)
    ///   앱 → 웹앱: state/*.json (최신 상태), log.jsonl (기록, 한 줄에 하나)
    /// </summary>
    public sealed class AdbFileChannel : IControlChannel
    {
        public string Name => "adb-file";
        public event Action<Dictionary<string, object>> ParamsReceived;
        public event Action<Dictionary<string, object>> ActionsReceived;

        const long MaxLogBytes = 5 * 1024 * 1024;
        const float PollInterval = 0.25f;

        string root, inbox, state, logPath;
        float nextPoll;
        bool open;
        readonly Dictionary<string, Dictionary<string, object>> patchCache = new Dictionary<string, Dictionary<string, object>>();

        public string RootPath => root;

        public void Open()
        {
            try
            {
                root = Path.Combine(Application.persistentDataPath, "vrlink");
                inbox = Path.Combine(root, "inbox");
                state = Path.Combine(root, "state");
                logPath = Path.Combine(root, "log.jsonl");
                Directory.CreateDirectory(inbox);
                Directory.CreateDirectory(state);
                open = true;
                Debug.Log($"[VRLink] ADB 파일 채널: {root}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VRLink] ADB 파일 채널을 열 수 없음: {e.Message}");
            }
        }

        public void PutState(string key, string json)
        {
            if (!open) return;
            if (Json.Parse(json) is Dictionary<string, object> d) patchCache[key] = d; else patchCache.Remove(key);
            WriteAtomic(Path.Combine(state, key + ".json"), json);
        }

        public void PatchState(string key, string json)
        {
            if (!open) return;
            if (!(Json.Parse(json) is Dictionary<string, object> patch)) return;
            if (!patchCache.TryGetValue(key, out var cur)) { cur = new Dictionary<string, object>(); patchCache[key] = cur; }
            foreach (var kv in patch) cur[kv.Key] = kv.Value;
            WriteAtomic(Path.Combine(state, key + ".json"), Json.Serialize(cur));
        }

        void WriteAtomic(string path, string text)
        {
            try
            {
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, text);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception e) { Debug.LogWarning($"[VRLink] 상태 저장 실패({Path.GetFileName(path)}): {e.Message}"); }
        }

        public void AppendLog(string json)
        {
            if (!open) return;
            try
            {
                var fi = new FileInfo(logPath);
                if (fi.Exists && fi.Length > MaxLogBytes)
                {
                    var old = Path.Combine(root, "log.1.jsonl");
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(logPath, old);
                }
                File.AppendAllText(logPath, json + "\n");
            }
            catch (Exception e) { Debug.LogWarning($"[VRLink] 기록 저장 실패: {e.Message}"); }
        }

        public void Tick()
        {
            if (!open || Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + PollInterval;
            ReadInbox("params.json", ParamsReceived);
            ReadInbox("actions.json", ActionsReceived);
        }

        void ReadInbox(string file, Action<Dictionary<string, object>> handler)
        {
            var path = Path.Combine(inbox, file);
            if (!File.Exists(path)) return;
            try
            {
                var text = File.ReadAllText(path);
                File.Delete(path);
                var dict = Json.ParseObject(text);
                if (dict != null) handler?.Invoke(dict);
            }
            catch (IOException) { /* adb push 진행 중일 수 있음 → 다음 폴링에서 재시도 */ }
            catch (Exception e)
            {
                Debug.LogWarning($"[VRLink] {file} 해석 실패: {e.Message}");
                try { File.Delete(path); } catch { }
            }
        }

        public void Close() { open = false; }
    }
}
