using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FunBrainLab.VRLink
{
    /// <summary>
    /// 씬에 하나만 두면 되는 연결 관리자. (메뉴: GameObject ▸ FunBrain ▸ VR Link 추가)
    /// - [Tunable] 변수 자동 수집 → 웹앱에 목록 전송, 값 변경 수신
    /// - VRLink.Log("이벤트", 데이터) 로 수행 기록 전송
    /// - USB(ADB 파일)와 네트워크(Firebase RTDB) 통로를 동시에 사용
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]
    public sealed class VRLink : MonoBehaviour
    {
        public const string PackageVersion = "0.4.0";

        [Header("연결 코드")]
        [Tooltip("AskOnFirstRun: 처음 실행 때 헤드셋에서 입력(웹앱 발급 코드, 병원용) · AutoFromDevice: 기기 ID로 자동(연구실 시험) · Fixed: 아래 고정값")]
#if VRLINK_PARTNER
        [HideInInspector]
#endif
        public PairingMode pairingMode = PairingMode.AskOnFirstRun;
        [Tooltip("Fixed 모드일 때만. 6자, I·O·0·1 제외. 기기 한 대 전용 빌드에서만 사용")]
#if VRLINK_PARTNER
        [HideInInspector]
#endif
        public string fixedCode = "";
        [Tooltip("코드 입력 키패드까지의 거리(m)")]
#if VRLINK_PARTNER
        [HideInInspector]
#endif
        public float keypadDistance = 1.6f;

        [Header("네트워크 (Firebase Realtime Database)")]
        [Tooltip("예: https://funbrainlab-apps-default-rtdb.asia-southeast1.firebasedatabase.app — 비우면 네트워크 통로를 끕니다")]
#if VRLINK_PARTNER
        [HideInInspector]
#endif
        public string databaseUrl = "";
        [Tooltip("RTDB 인증 토큰(선택). 연구실 표준 설정은 README 참고")]
#if VRLINK_PARTNER
        [HideInInspector]
#endif
        public string authToken = "";
#if VRLINK_PARTNER
        [HideInInspector]
#endif
        public bool useNetwork = true;

        [Header("USB (ADB 파일)")]
#if VRLINK_PARTNER
        [HideInInspector]
#endif
        public bool useAdbFiles = true;

        [Header("WebRTC ICE 서버")]
#if VRLINK_PARTNER
        [HideInInspector]
#endif
        public string stunUrl = "stun:stun.l.google.com:19302";
        [Tooltip("병원망처럼 방화벽이 강한 곳에서는 TURN 이 필요합니다. 예: turn:turn.brainz.fit:3478")]
#if VRLINK_PARTNER
        [HideInInspector]
#endif
        public string turnUrl = "";
#if VRLINK_PARTNER
        [HideInInspector]
#endif
        public string turnUsername = "";
#if VRLINK_PARTNER
        [HideInInspector]
#endif
        public string turnCredential = "";

        [Header("기타")]
        [Tooltip("상태(FPS 등) 전송 간격(초)")]
#if VRLINK_PARTNER
        [HideInInspector]
#endif
        public float statusInterval = 5f;
        [Tooltip("씬이 바뀌면 [Tunable]/[Monitor]/[TunableAction] 다시 수집")]
#if VRLINK_PARTNER
        [HideInInspector]
#endif
        public bool rescanOnSceneLoad = true;
        [Tooltip("[Monitor] 값 변경 확인 주기(초). 각 변수의 Interval 보다 작아야 함")]
#if VRLINK_PARTNER
        [HideInInspector]
#endif
        public float monitorPollInterval = 0.1f;

        public static VRLink Instance { get; private set; }
        /// <summary>id, 새 값 — 웹앱에서 값이 바뀌어 적용된 직후 호출</summary>
        public static event Action<string, object> ParamChanged;
        /// <summary>actionId — 웹앱 버튼으로 동작이 실행된 직후 호출</summary>
        public static event Action<string> ActionInvoked;
        /// <summary>WebRTC 시그널 수신 (WebRtcStreamer 가 사용)</summary>
        public event Action<Dictionary<string, object>> SignalReceived;

        public string PairingCode { get; private set; }
        /// <summary>코드가 정해지고 통로가 열렸는지. 키패드 입력 전에는 false</summary>
        public bool IsLinked { get; private set; }
        public DeviceInfo Device { get; private set; }
        public TunableRegistry Registry { get; } = new TunableRegistry();
        public RtdbChannel Network { get; private set; }
        public AdbFileChannel Adb { get; private set; }

        public static bool WebRtcAvailable
        {
#if VRLINK_WEBRTC
            get => true;
#else
            get => false;
#endif
        }

        readonly List<IControlChannel> channels = new List<IControlChannel>();
        readonly Queue<Dictionary<string, object>> pendingParams = new Queue<Dictionary<string, object>>();
        readonly Queue<Dictionary<string, object>> pendingActions = new Queue<Dictionary<string, object>>();
        float nextMonitorPoll;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
#if VRLINK_PARTNER
            // 협력업체용 빌드: 설정은 DLL 에 고정, 인스펙터에서 바꿀 수 없음
            PartnerConfig.Apply(this);
#endif
            DontDestroyOnLoad(gameObject);

            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        IEnumerator Start()
        {
            // XR 초기화가 끝난 뒤 감지해야 눈 해상도·주사율이 정확합니다.
            yield return null;
            Device = DeviceInfo.Detect();
            if (Device.RefreshRate > 0) FrameStats.TargetRate = Device.RefreshRate;

            var code = PairingStore.Resolve(pairingMode, fixedCode);
            if (code != null) { Link(code); yield break; }

            Debug.Log("[VRLink] 저장된 연결 코드 없음 → 키패드 표시");
            var pad = PairingKeypad.Show(transform, c => { PairingStore.Save(c); Link(c); });
            pad.distance = keypadDistance;
        }

        /// <summary>코드가 정해지면 통로를 열고 보고를 시작한다</summary>
        void Link(string code)
        {
            PairingCode = code;
            if (useAdbFiles) { Adb = new AdbFileChannel(); channels.Add(Adb); }
            if (useNetwork && !string.IsNullOrWhiteSpace(databaseUrl))
            {
                Network = new RtdbChannel(this, databaseUrl.Trim(), authToken, code);
                Network.SignalReceived += m => SignalReceived?.Invoke(m);
                channels.Add(Network);
            }
            foreach (var c in channels)
            {
                c.ParamsReceived += p => pendingParams.Enqueue(p);
                c.ActionsReceived += a => pendingActions.Enqueue(a);
                c.Open();
            }
            IsLinked = true;
            Debug.Log($"[VRLink] 코드 {code} ({pairingMode}) | {Device}");
            PublishHello();
            Rescan();
            StartCoroutine(StatusLoop());
        }

        /// <summary>저장된 코드를 지우고 다시 묻는다 (다른 프로젝트로 옮길 때). 앱을 다시 시작해야 적용됨</summary>
        public static void ResetPairingCode()
        {
            PairingStore.Clear();
            Debug.Log("[VRLink] 연결 코드 삭제됨 — 다음 실행 때 다시 묻습니다");
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            foreach (var c in channels) c.Close();
            Instance = null;
        }

        void OnSceneLoaded(Scene s, LoadSceneMode mode)
        {
            if (rescanOnSceneLoad && IsLinked) Rescan();
        }

        void Update()
        {
            FrameStats.Tick();
            foreach (var c in channels) c.Tick();
            while (pendingParams.Count > 0) ApplyParams(pendingParams.Dequeue());
            while (pendingActions.Count > 0) RunActions(pendingActions.Dequeue());

            if (IsLinked && Time.unscaledTime >= nextMonitorPoll)
            {
                nextMonitorPoll = Time.unscaledTime + Mathf.Max(0.02f, monitorPollInterval);
                var changed = Registry.CollectChangedMonitors(Time.unscaledTime);
                if (changed != null)
                {
                    var json = Json.Serialize(changed);
                    foreach (var c in channels) c.PatchState("monitors", json);
                }
            }
        }

        // ---------------- 공개 API ----------------

        /// <summary>수행 기록 한 건. 예) VRLink.Log("trial_end", new Dictionary&lt;string,object&gt;{{"score",8},{"rt",0.42f}})</summary>
        public static void Log(string evt, object data = null)
        {
            var inst = Instance;
            if (inst == null || !inst.IsLinked) return;
            var e = new Dictionary<string, object>
            {
                ["t"] = DateTime.UtcNow.ToString("o"),
                ["ts"] = Time.realtimeSinceStartupAsDouble,
                ["event"] = evt,
                ["scene"] = SceneManager.GetActiveScene().name
            };
            if (data != null) e["data"] = data;
            var json = Json.Serialize(e);
            foreach (var c in inst.channels) c.AppendLog(json);
        }

        /// <summary>[Tunable]/[Monitor]/[TunableAction] 다시 수집 (런타임에 오브젝트를 생성한 뒤 호출)</summary>
        public void Rescan()
        {
            if (!IsLinked) return;
            Registry.Scan();
            var schema = Json.Serialize(Registry.BuildSchema());
            var monitors = Json.Serialize(Registry.BuildMonitorValues(Time.unscaledTime));
            foreach (var c in channels)
            {
                c.PutState("schema", schema);
                c.PutState("monitors", monitors);
            }
            PublishValues();
        }

        /// <summary>코드에서 동작을 실행했을 때 웹앱 기록에도 남기고 싶으면 호출</summary>
        public static void NotifyAction(string actionId) => Log("action", actionId);

        /// <summary>코드에서 값을 바꾼 뒤 웹앱 화면도 갱신하고 싶을 때</summary>
        public void PublishValues()
        {
            var values = Json.Serialize(Registry.BuildValues());
            foreach (var c in channels) c.PutState("values", values);
        }

        // ---------------- 내부 ----------------

        void ApplyParams(Dictionary<string, object> p)
        {
            var changed = Registry.Apply(p);
            if (changed.Count == 0) return;
            var applied = new Dictionary<string, object>();
            foreach (var id in changed)
            {
                if (!Registry.TryGetTunable(id, out var e)) continue;
                var v = e.GetForWire();
                applied[id] = v;
                try { ParamChanged?.Invoke(id, e.Get()); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
            PublishValues();
            Log("param_applied", applied);
        }

        readonly Dictionary<string, string> lastActionNonce = new Dictionary<string, string>();

        void RunActions(Dictionary<string, object> cmds)
        {
            foreach (var kv in cmds)
            {
                var nonce = kv.Value?.ToString();
                if (string.IsNullOrEmpty(nonce)) continue;
                if (lastActionNonce.TryGetValue(kv.Key, out var prev) && prev == nonce) continue; // 중복 수신 방지
                lastActionNonce[kv.Key] = nonce;
                if (!Registry.TryGetAction(kv.Key, out var a)) { Log("action_unknown", kv.Key); continue; }
                var ok = a.Invoke();
                Log(ok ? "action" : "action_failed", kv.Key);
                if (ok) { try { ActionInvoked?.Invoke(kv.Key); } catch (Exception ex) { Debug.LogException(ex); } }
            }
        }

        void PublishHello()
        {
            var hello = new Dictionary<string, object>
            {
                ["code"] = PairingCode,
                ["app"] = Application.identifier,
                ["appVersion"] = Application.version,
                ["productName"] = Application.productName,
                ["unity"] = Application.unityVersion,
                ["vrlink"] = PackageVersion,
                ["webrtc"] = WebRtcAvailable,
                ["device"] = Device.ToDict(),
                ["startedAt"] = DateTime.UtcNow.ToString("o")
            };
            var json = Json.Serialize(hello);
            foreach (var c in channels) c.PutState("hello", json);
        }

        IEnumerator StatusLoop()
        {
            var wait = new WaitForSecondsRealtime(Mathf.Max(1f, statusInterval));
            while (true)
            {
                yield return wait;
                var st = new Dictionary<string, object>
                {
                    ["t"] = DateTime.UtcNow.ToString("o"),
                    ["fps"] = Math.Round(FrameStats.Fps, 1),
                    ["slowFrames"] = FrameStats.SlowFramesLastSecond,
                    ["targetHz"] = FrameStats.TargetRate,
                    ["scene"] = SceneManager.GetActiveScene().name,
                    ["battery"] = SystemInfo.batteryLevel,
                    ["thermal"] = ThermalState()
                };
                var json = Json.Serialize(st);
                foreach (var c in channels) c.PutState("status", json);
            }
        }

        static string ThermalState()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unity.GetStatic<AndroidJavaObject>("currentActivity");
                using var pm = activity.Call<AndroidJavaObject>("getSystemService", "power");
                return pm.Call<int>("getCurrentThermalStatus").ToString();
            }
            catch { return "unknown"; }
#else
            return "n/a";
#endif
        }

    }
}
