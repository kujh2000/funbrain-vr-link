using System.Collections.Generic;
using UnityEngine;

namespace FunBrainLab.VRLink.Probe
{
    /// <summary>
    /// 0단계 실기기 검증용. 빈 VR 씬에 VR Link 와 함께 넣고 빌드하세요.
    /// - 헤드셋 앞에 페어링 코드·FPS·기기 정보 표시
    /// - [Tunable] 5종으로 웹앱 조절 확인, [Monitor] 7종으로 위젯 표시 확인, [TunableAction] 3종으로 버튼 확인
    /// - loadLevel 로 큐브를 늘려 스트리밍 중 VR 프레임 유지 여부 확인
    /// - 1초마다 probe_fps 기록 전송
    /// </summary>
    public sealed class VRLinkProbe : MonoBehaviour
    {
        public enum CubeColor { White, Cyan, Yellow, Magenta }
        public enum Phase { Idle, Running, Paused, Done }

        // ---------- 조절 (웹 → 앱) ----------
        [Tunable(0.05f, 1f, Label = "큐브 크기", Group = "검증", Step = 0.05f, Unit = "m", Order = 1)]
        public float cubeScale = 0.3f;

        [Tunable(0f, 360f, Label = "회전 속도", Group = "검증", Unit = "°/s", Widget = "knob", Order = 2)]
        public float rotationSpeed = 45f;

        [Tunable(Label = "큐브 표시", Group = "검증", Order = 3)]
        public bool showCube = true;

        [Tunable(Label = "큐브 색", Group = "검증", Widget = "buttons", Order = 4)]
        public CubeColor color = CubeColor.White;

        [Tunable(0, 20, Label = "부하 레벨", Group = "성능", Widget = "stepper", Help = "x100 큐브. 스트리밍 중 프레임 저하 확인용")]
        public int loadLevel = 0;

        // ---------- 상태 (앱 → 웹) ----------
        [Monitor(Label = "진행 단계", Group = "상태", Widget = "trafficlight", Colors = "gray,green,yellow,blue", Order = 1)]
        public Phase phase = Phase.Idle;

        [Monitor(Label = "진행률", Group = "상태", Widget = "ring", Min = 0, Max = 100, Unit = "%", Decimals = 0, Order = 2)]
        public float progress;

        [Monitor(Label = "성공 횟수", Group = "상태", Widget = "number", Unit = "회", Decimals = 0, Order = 3)]
        public int successCount;

        [Monitor(Label = "FPS", Group = "성능", Widget = "sparkline", Min = 0, Max = 120, Decimals = 0, Interval = 1f)]
        public float fps;

        [Monitor(Label = "느린 프레임", Group = "성능", Widget = "trafficlight", Thresholds = "1,5", Decimals = 0, Interval = 1f, Help = "1초 동안 목표의 1.5배보다 느렸던 프레임 수")]
        public int slowFrames;

        [Monitor(Label = "경과 시간", Group = "상태", Widget = "timer", Decimals = 0, Interval = 1f)]
        public float elapsed;

        [Monitor(Label = "시선 방향", Group = "상태", Widget = "xy", Min = -1, Max = 1, Interval = 0.2f)]
        public Vector2 gazeXY;

        [Monitor(Label = "스트리밍", Group = "성능", Widget = "led", Interval = 1f)]
        public bool streaming;

        // ---------- 동작 (웹 → 앱) ----------
        [TunableAction(Label = "시작", Group = "제어", Widget = "primary", Order = 1)]
        void StartRun() { phase = Phase.Running; progress = 0f; successCount = 0; elapsed = 0f; VRLink.Log("run_start"); }

        [TunableAction(Label = "일시정지", Group = "제어", Order = 2)]
        void PauseRun() { if (phase == Phase.Running) phase = Phase.Paused; else if (phase == Phase.Paused) phase = Phase.Running; }

        [TunableAction(Label = "초기화", Group = "제어", Widget = "danger", Confirm = true, Order = 3)]
        void ResetRun() { phase = Phase.Idle; progress = 0f; successCount = 0; elapsed = 0f; loadLevel = 0; VRLink.Log("run_reset"); }

        [Tooltip("헤드셋 기준 정보 패널 위치")]
        public Vector3 panelOffset = new Vector3(0f, 0.25f, 1.5f);

        GameObject cube;
        Material cubeMat;
        TextMesh info;
        readonly List<GameObject> loadCubes = new List<GameObject>();
        float nextReport, nextSuccess;

        void Start()
        {
            cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Probe Cube";
            cube.transform.SetParent(transform, false);
            cube.transform.localPosition = new Vector3(0f, 1.4f, 1.5f);
            cubeMat = cube.GetComponent<Renderer>().material;

            var textGo = new GameObject("Probe Info");
            info = textGo.AddComponent<TextMesh>();
            info.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            textGo.GetComponent<MeshRenderer>().sharedMaterial = info.font.material;
            info.fontSize = 64;
            info.characterSize = 0.006f;
            info.anchor = TextAnchor.MiddleCenter;
            info.alignment = TextAlignment.Center;
            info.color = Color.white;
        }

        void Update()
        {
            cube.SetActive(showCube);
            cube.transform.localScale = Vector3.one * cubeScale;
            cube.transform.Rotate(0f, rotationSpeed * Time.deltaTime, 0f, Space.World);
            cubeMat.color = color switch
            {
                CubeColor.Cyan => Color.cyan,
                CubeColor.Yellow => Color.yellow,
                CubeColor.Magenta => Color.magenta,
                _ => Color.white
            };
            UpdateLoad();
            UpdatePanel();
            UpdateDemoState();

            var link = VRLink.Instance;
            fps = FrameStats.Fps;
            slowFrames = FrameStats.SlowFramesLastSecond;
            streaming = link != null && link.TryGetComponent<WebRtcStreamer>(out var s) && s.IsStreaming;
            var head = Camera.main != null ? Camera.main.transform : null;
            if (head != null) gazeXY = new Vector2(head.forward.x, head.forward.y);

            if (Time.unscaledTime >= nextReport)
            {
                nextReport = Time.unscaledTime + 1f;
                VRLink.Log("probe_fps", new Dictionary<string, object>
                {
                    ["fps"] = System.Math.Round(fps, 1),
                    ["slowFrames"] = slowFrames,
                    ["targetHz"] = FrameStats.TargetRate,
                    ["loadCubes"] = loadCubes.Count,
                    ["streaming"] = streaming
                });
            }
        }

        /// <summary>시작 버튼을 누르면 30초 동안 진행률이 차고 2초마다 성공 횟수가 늘어나는 가짜 과제</summary>
        void UpdateDemoState()
        {
            if (phase != Phase.Running) return;
            elapsed += Time.deltaTime;
            progress = Mathf.Min(100f, elapsed / 30f * 100f);
            if (Time.time >= nextSuccess)
            {
                nextSuccess = Time.time + 2f;
                successCount++;
                VRLink.Log("trial_end", new Dictionary<string, object> { ["score"] = successCount, ["rt"] = Random.Range(0.3f, 0.8f) });
            }
            if (progress >= 100f) { phase = Phase.Done; VRLink.Log("run_done", successCount); }
        }

        void UpdateLoad()
        {
            int target = Mathf.Clamp(loadLevel, 0, 20) * 100;
            while (loadCubes.Count < target)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(c.GetComponent<Collider>());
                c.transform.SetParent(transform, false);
                c.transform.localScale = Vector3.one * 0.05f;
                c.transform.localPosition = Random.insideUnitSphere * 2f + new Vector3(0f, 1.4f, 3f);
                loadCubes.Add(c);
            }
            while (loadCubes.Count > target)
            {
                var last = loadCubes[loadCubes.Count - 1];
                loadCubes.RemoveAt(loadCubes.Count - 1);
                Destroy(last);
            }
        }

        void UpdatePanel()
        {
            var head = Camera.main != null ? Camera.main.transform : null;
            if (head != null)
            {
                var fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
                if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
                info.transform.position = head.position + fwd * panelOffset.z + Vector3.up * panelOffset.y;
                info.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            }
            var link = VRLink.Instance;
            // HMD 텍스트는 기본 폰트 호환을 위해 영문만 사용
            info.text =
                $"CODE  {(link != null ? link.PairingCode : "------")}\n" +
                $"{FrameStats.Fps:F0} fps / {FrameStats.TargetRate:F0} Hz   slow {FrameStats.SlowFramesLastSecond}\n" +
                $"{(link != null && link.Device != null ? link.Device.ToString() : "detecting...")}\n" +
                $"net {(link?.Network == null ? "off" : link.Network.Connected ? "ok" : "...")}   " +
                $"webrtc {(VRLink.WebRtcAvailable ? (streaming ? "LIVE" : "ready") : "n/a")}   " +
                $"{phase} {progress:F0}%  load {loadCubes.Count}";
        }
    }
}
