using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace FunBrainLab.VRLink
{
    /// <summary>
    /// 네트워크 통로: Firebase Realtime Database REST + 실시간 스트리밍(SSE).
    /// Firebase Unity SDK 없이 UnityWebRequest만 사용하므로 Vive·Quest 모두 동일하게 동작합니다.
    ///
    /// 경로 (vrlink/devices/{페어링코드}/...)
    ///   hello, schema, values, status     앱 → 웹 (PUT, 최신 상태)
    ///   logs                              앱 → 웹 (POST, 기록)
    ///   control/params                    웹 → 앱 ({id: 값}, 실시간 수신)
    ///   signal/toDevice                   웹 → 앱 (WebRTC 시그널링)
    ///   signal/toWeb                      앱 → 웹 (WebRTC 시그널링)
    /// </summary>
    public sealed class RtdbChannel : IControlChannel
    {
        public string Name => "rtdb";
        public event Action<Dictionary<string, object>> ParamsReceived;
        public event Action<Dictionary<string, object>> ActionsReceived;
        /// <summary>signal/toDevice 에 새 메시지가 오면 호출</summary>
        public event Action<Dictionary<string, object>> SignalReceived;

        readonly MonoBehaviour host;
        readonly string baseUrl;
        readonly string devicePath;
        string authToken;
        bool open;
        readonly List<UnityWebRequest> streams = new List<UnityWebRequest>();

        public bool Connected { get; private set; }

        public RtdbChannel(MonoBehaviour host, string databaseUrl, string authToken, string pairingCode)
        {
            this.host = host;
            baseUrl = databaseUrl.TrimEnd('/');
            this.authToken = authToken;
            devicePath = $"vrlink/devices/{pairingCode}";
        }

        /// <summary>페어링 후 웹앱이 발급한 토큰으로 교체할 때 사용</summary>
        public void SetAuthToken(string token) { authToken = token; }

        string Url(string path)
        {
            var url = $"{baseUrl}/{path}.json";
            if (!string.IsNullOrEmpty(authToken)) url += "?auth=" + UnityWebRequest.EscapeURL(authToken);
            return url;
        }

        public void Open()
        {
            open = true;
            host.StartCoroutine(Listen($"{devicePath}/control/params", OnParamsEvent));
            host.StartCoroutine(Listen($"{devicePath}/control/actions", OnActionsEvent));
            host.StartCoroutine(Listen($"{devicePath}/signal/toDevice", OnSignalEvent));
        }

        public void PutState(string key, string json) => Send("PUT", $"{devicePath}/{key}", json);
        public void PatchState(string key, string json) => Send("PATCH", $"{devicePath}/{key}", json);
        public void AppendLog(string json) => Send("POST", $"{devicePath}/logs", json);
        public void SendSignal(string json) => Send("POST", $"{devicePath}/signal/toWeb", json);

        /// <summary>새 스트리밍 세션 시작 전에 이전 시그널 메시지 정리</summary>
        public void ClearSignals()
        {
            Send("DELETE", $"{devicePath}/signal/toWeb", null);
        }

        public void Tick() { }

        public void Close()
        {
            open = false;
            foreach (var r in streams) { try { r.Abort(); } catch { } }
            streams.Clear();
        }

        // ---------------- 쓰기 ----------------
        void Send(string method, string path, string json)
        {
            if (!open || !host.isActiveAndEnabled) return;
            host.StartCoroutine(SendRoutine(method, path, json));
        }

        IEnumerator SendRoutine(string method, string path, string json)
        {
            using var req = new UnityWebRequest(Url(path), method);
            if (json != null)
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                req.SetRequestHeader("Content-Type", "application/json");
            }
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = 15;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
                Debug.LogWarning($"[VRLink] RTDB {method} {path} 실패: {req.responseCode} {req.error}");
        }

        // ---------------- 실시간 수신 (SSE) ----------------
        IEnumerator Listen(string path, Action<string, string, object> onEvent)
        {
            float backoff = 1f;
            while (open)
            {
                var handler = new SseHandler(onEvent);
                var req = new UnityWebRequest(Url(path), "GET") { downloadHandler = handler };
                req.SetRequestHeader("Accept", "text/event-stream");
                streams.Add(req);
                var started = Time.realtimeSinceStartup;
                yield return req.SendWebRequest();
                streams.Remove(req);

                if (handler.GotData) { Connected = true; }
                if (open)
                {
                    Connected = false;
                    if (Time.realtimeSinceStartup - started > 30f) backoff = 1f;
                    Debug.Log($"[VRLink] RTDB 스트림 재연결 ({path}): {req.error} — {backoff:F0}초 후");
                }
                req.Dispose();
                if (!open) yield break;
                yield return new WaitForSecondsRealtime(backoff);
                backoff = Mathf.Min(backoff * 2f, 30f);
            }
        }

        readonly Dictionary<string, object> paramCache = new Dictionary<string, object>();

        void OnParamsEvent(string evt, string path, object data)
        {
            Connected = true;
            var changed = new Dictionary<string, object>();
            if (path == "/")
            {
                if (evt == "put") paramCache.Clear();
                if (data is Dictionary<string, object> all)
                    foreach (var kv in all) { paramCache[kv.Key] = kv.Value; changed[kv.Key] = kv.Value; }
            }
            else
            {
                var key = path.Trim('/');
                int slash = key.IndexOf('/');
                if (slash >= 0) key = key.Substring(0, slash); // 중첩 경로는 첫 단계만
                paramCache[key] = data;
                changed[key] = data;
            }
            if (changed.Count > 0) ParamsReceived?.Invoke(changed);
        }

        bool actionsPrimed;

        /// <summary>control/actions/{actionId} = 요청번호. 연결 직후 첫 전체 스냅샷은 과거 명령이므로 무시</summary>
        void OnActionsEvent(string evt, string path, object data)
        {
            Connected = true;
            if (path == "/" && !actionsPrimed) { actionsPrimed = true; return; }
            actionsPrimed = true;
            var cmds = new Dictionary<string, object>();
            if (path == "/")
            {
                if (data is Dictionary<string, object> all) foreach (var kv in all) cmds[kv.Key] = kv.Value;
            }
            else
            {
                var key = path.Trim('/');
                int slash = key.IndexOf('/');
                if (slash >= 0) key = key.Substring(0, slash);
                if (data != null) cmds[key] = data;
            }
            if (cmds.Count > 0) ActionsReceived?.Invoke(cmds);
        }

        bool signalPrimed;

        void OnSignalEvent(string evt, string path, object data)
        {
            Connected = true;
            // 연결 직후 첫 "/" put 은 기존에 쌓인 메시지 → 무시
            if (path == "/" && !signalPrimed) { signalPrimed = true; return; }
            signalPrimed = true;
            if (path == "/")
            {
                if (data is Dictionary<string, object> many)
                    foreach (var kv in many)
                        if (kv.Value is Dictionary<string, object> m) SignalReceived?.Invoke(m);
                return;
            }
            if (data is Dictionary<string, object> msg) SignalReceived?.Invoke(msg);
        }

        /// <summary>Firebase 스트리밍 응답(event:/data: 줄)을 해석</summary>
        sealed class SseHandler : DownloadHandlerScript
        {
            readonly Action<string, string, object> onEvent;
            readonly Decoder decoder = Encoding.UTF8.GetDecoder();
            readonly StringBuilder line = new StringBuilder();
            char[] chars = new char[8192];
            string curEvent, curData;
            public bool GotData { get; private set; }

            public SseHandler(Action<string, string, object> onEvent) : base(new byte[8192]) { this.onEvent = onEvent; }

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (data == null || dataLength == 0) return true;
                GotData = true;
                int need = decoder.GetCharCount(data, 0, dataLength);
                if (need > chars.Length) chars = new char[need];
                int n = decoder.GetChars(data, 0, dataLength, chars, 0);
                for (int i = 0; i < n; i++)
                {
                    char c = chars[i];
                    if (c == '\r') continue;
                    if (c != '\n') { line.Append(c); continue; }
                    HandleLine(line.ToString());
                    line.Clear();
                }
                return true;
            }

            void HandleLine(string l)
            {
                if (l.Length == 0)
                {
                    Dispatch();
                    return;
                }
                if (l.StartsWith("event:")) curEvent = l.Substring(6).Trim();
                else if (l.StartsWith("data:")) curData = l.Substring(5).Trim();
            }

            void Dispatch()
            {
                var evt = curEvent; var dataText = curData;
                curEvent = null; curData = null;
                if (evt == null) return;
                if (evt == "keep-alive") return;
                if (evt == "cancel" || evt == "auth_revoked")
                {
                    Debug.LogWarning($"[VRLink] RTDB 스트림 {evt}: 보안 규칙이나 인증 토큰을 확인하세요.");
                    return;
                }
                if (evt != "put" && evt != "patch") return;
                try
                {
                    if (Json.Parse(dataText) is Dictionary<string, object> payload)
                    {
                        var path = payload.TryGetValue("path", out var p) ? p as string ?? "/" : "/";
                        payload.TryGetValue("data", out var d);
                        onEvent?.Invoke(evt, path, d);
                    }
                }
                catch (Exception e) { Debug.LogWarning($"[VRLink] RTDB 이벤트 해석 실패: {e.Message}"); }
            }
        }
    }
}
