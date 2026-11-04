using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if VRLINK_WEBRTC
using System.Linq;
using Unity.WebRTC;
#endif

namespace FunBrainLab.VRLink
{
    /// <summary>
    /// 헤드셋 화면을 웹앱으로 WebRTC 스트리밍합니다.
    ///
    /// 다양한 환경 대응 방식: XR 눈 텍스처를 직접 꺼내지 않고, 머리 카메라를 따라다니는
    /// "관찰용 모노 카메라"를 저해상도로 따로 렌더링합니다. 그래서 그래픽 API(Vulkan/GLES)와
    /// 스테레오 방식(Multi-pass/Single Pass Instanced/Multiview)에 상관없이 같은 코드로 동작합니다.
    /// 대가로 GPU 를 조금 더 씁니다 → 해상도·FPS 를 낮게 시작하고 0단계 검증에서 조정하세요.
    ///
    /// com.unity.webrtc 패키지가 없으면 자동으로 꺼집니다(컴파일 오류 없음).
    /// </summary>
    [RequireComponent(typeof(VRLink))]
    public sealed class WebRtcStreamer : MonoBehaviour
    {
        [Header("영상")]
        public int width = 1280;
        public int height = 720;
        [Range(5, 60)] public int maxFramerate = 30;
        [Tooltip("kbps")] public int maxBitrateKbps = 3000;
        [Range(40, 110)] public float fieldOfView = 90f;
        [Tooltip("H.264 우선 (대부분의 안드로이드 헤드셋에서 하드웨어 인코더 사용)")]
        public bool preferH264 = true;

        [Header("관찰 카메라")]
        [Tooltip("비우면 Camera.main 을 따라갑니다")]
        public Transform followTarget;
        [Tooltip("관찰 카메라를 렌더링할 레이어 (비우면 머리 카메라와 동일)")]
        public LayerMask cullingMaskOverride = 0;

        public bool IsStreaming { get; private set; }

#if VRLINK_WEBRTC
        VRLink link;
        Camera spectator;
        RTCPeerConnection pc;
        VideoStreamTrack track;
        bool remoteSet;
        string session = "";
        /// <summary>웹앱이 request 에 실어 보낸 ICE 서버(STUN/TURN 임시 자격증명). 헤드셋에는 저장하지 않는다</summary>
        List<Dictionary<string, object>> requestedIce;
        readonly List<RTCIceCandidate> pendingRemote = new List<RTCIceCandidate>();

        IEnumerator Start()
        {
            link = GetComponent<VRLink>();
            yield return null; // VRLink.Start 이후
            if (link.Network == null)
            {
                Debug.LogWarning("[VRLink] 네트워크 통로(databaseUrl)가 없어 스트리밍 시그널링을 할 수 없습니다.");
                enabled = false;
                yield break;
            }
            link.SignalReceived += OnSignal;
            StartCoroutine(WebRTC.Update());
            Debug.Log("[VRLink] WebRTC 대기 중 — 웹앱에서 '화면 보기'를 누르면 시작합니다.");
        }

        void OnDestroy()
        {
            if (link != null) link.SignalReceived -= OnSignal;
            StopSession();
            if (spectator != null) Destroy(spectator.gameObject);
        }

        void LateUpdate()
        {
            if (spectator == null) return;
            var target = followTarget != null ? followTarget : (Camera.main != null ? Camera.main.transform : null);
            if (target == null) return;
            spectator.transform.SetPositionAndRotation(target.position, target.rotation);
        }

        void OnSignal(Dictionary<string, object> m)
        {
            var type = m.TryGetValue("type", out var t) ? t as string : null;
            var sess = m.TryGetValue("session", out var sv) ? sv as string ?? "" : "";
            // 다른 창·PC 의 세션 메시지는 무시 (request 만 새 세션을 연다)
            if (type != "request" && sess != session) return;
            switch (type)
            {
                case "request":
                    session = sess;
                    requestedIce = null;
                    if (m.TryGetValue("iceServers", out var ic) && ic is List<object> list)
                    {
                        requestedIce = new List<Dictionary<string, object>>();
                        foreach (var item in list) if (item is Dictionary<string, object> d) requestedIce.Add(d);
                    }
                    StartCoroutine(StartSession());
                    break;
                case "answer": StartCoroutine(SetAnswer(m.TryGetValue("sdp", out var s) ? s as string : null)); break;
                case "candidate": AddRemoteCandidate(m); break;
                case "stop": StopSession(); VRLink.Log("webrtc_stopped"); break;
            }
        }

        void EnsureSpectator()
        {
            if (spectator != null) return;
            var head = Camera.main;
            var go = new GameObject("VRLink Spectator Camera");
            go.transform.SetParent(transform, false);
            spectator = go.AddComponent<Camera>();
            spectator.stereoTargetEye = StereoTargetEyeMask.None; // 모노 렌더링
            spectator.fieldOfView = fieldOfView;
            if (head != null)
            {
                spectator.clearFlags = head.clearFlags;
                spectator.backgroundColor = head.backgroundColor;
                spectator.cullingMask = head.cullingMask;
                spectator.nearClipPlane = head.nearClipPlane;
                spectator.farClipPlane = head.farClipPlane;
                spectator.depth = head.depth - 1;
            }
            if (cullingMaskOverride.value != 0) spectator.cullingMask = cullingMaskOverride.value;
        }

        IEnumerator StartSession()
        {
            StopSession();
            link.Network.ClearSignals();
            EnsureSpectator();
            spectator.enabled = true;

            var config = new RTCConfiguration { iceServers = BuildIceServers() };
            pc = new RTCPeerConnection(ref config);
            pc.OnIceCandidate = c =>
            {
                link.Network.SendSignal(Json.Serialize(new Dictionary<string, object>
                {
                    ["type"] = "candidate",
                    ["candidate"] = c.Candidate,
                    ["sdpMid"] = c.SdpMid,
                    ["sdpMLineIndex"] = c.SdpMLineIndex,
                    ["session"] = session
                }));
            };
            pc.OnIceConnectionChange = s =>
            {
                IsStreaming = s == RTCIceConnectionState.Connected || s == RTCIceConnectionState.Completed;
                VRLink.Log("webrtc_ice", s.ToString());
            };

            track = spectator.CaptureStreamTrack(width, height);
            var sender = pc.AddTrack(track);
            ApplyCodecPreference(sender);
            ApplyEncoding(sender);

            var offerOp = pc.CreateOffer();
            yield return offerOp;
            if (offerOp.IsError) { Fail("offer 생성", offerOp.Error.message); yield break; }
            var desc = offerOp.Desc;
            var setOp = pc.SetLocalDescription(ref desc);
            yield return setOp;
            if (setOp.IsError) { Fail("local description", setOp.Error.message); yield break; }

            link.Network.SendSignal(Json.Serialize(new Dictionary<string, object>
            {
                ["type"] = "offer",
                ["sdp"] = desc.sdp,
                ["width"] = width,
                ["height"] = height,
                ["device"] = link.Device != null ? link.Device.Kind : "Unknown",
                ["session"] = session
            }));
            VRLink.Log("webrtc_offer_sent", new Dictionary<string, object> { ["w"] = width, ["h"] = height, ["fps"] = maxFramerate });
        }

        IEnumerator SetAnswer(string sdp)
        {
            if (pc == null || string.IsNullOrEmpty(sdp)) yield break;
            var d = new RTCSessionDescription { type = RTCSdpType.Answer, sdp = sdp };
            var op = pc.SetRemoteDescription(ref d);
            yield return op;
            if (op.IsError) { Fail("answer 적용", op.Error.message); yield break; }
            remoteSet = true;
            foreach (var c in pendingRemote) pc.AddIceCandidate(c);
            pendingRemote.Clear();
        }

        void AddRemoteCandidate(Dictionary<string, object> m)
        {
            if (pc == null) return;
            var init = new RTCIceCandidateInit
            {
                candidate = m.TryGetValue("candidate", out var c) ? c as string : null,
                sdpMid = m.TryGetValue("sdpMid", out var mid) ? mid as string : null,
                sdpMLineIndex = m.TryGetValue("sdpMLineIndex", out var idx) && idx != null ? (int?)System.Convert.ToInt32(idx) : null
            };
            if (string.IsNullOrEmpty(init.candidate)) return;
            var cand = new RTCIceCandidate(init);
            if (remoteSet) pc.AddIceCandidate(cand);
            else pendingRemote.Add(cand);
        }

        void ApplyCodecPreference(RTCRtpSender sender)
        {
            if (!preferH264) return;
            try
            {
                var transceiver = pc.GetTransceivers().FirstOrDefault(t => t.Sender == sender);
                if (transceiver == null) return;
                var codecs = RTCRtpSender.GetCapabilities(TrackKind.Video).codecs;
                var ordered = codecs.Where(c => c.mimeType == "video/H264")
                                    .Concat(codecs.Where(c => c.mimeType != "video/H264"))
                                    .ToArray();
                transceiver.SetCodecPreferences(ordered);
            }
            catch (System.Exception e) { Debug.LogWarning($"[VRLink] 코덱 우선순위 설정 실패: {e.Message}"); }
        }

        void ApplyEncoding(RTCRtpSender sender)
        {
            var p = sender.GetParameters();
            foreach (var enc in p.encodings)
            {
                enc.maxBitrate = (ulong)maxBitrateKbps * 1000UL;
                enc.maxFramerate = (uint)maxFramerate;
            }
            sender.SetParameters(p);
        }

        RTCIceServer[] BuildIceServers()
        {
            var list = new List<RTCIceServer>();
            // 웹앱이 보낸 서버가 있으면 그것을 우선 사용 (서버가 발급한 2시간짜리 TURN 자격증명 포함)
            if (requestedIce != null && requestedIce.Count > 0)
            {
                foreach (var d in requestedIce)
                {
                    var urls = new List<string>();
                    if (d.TryGetValue("urls", out var u))
                    {
                        if (u is List<object> ul) { foreach (var x in ul) if (x != null) urls.Add(x.ToString()); }
                        else if (u is string us) urls.Add(us);
                    }
                    if (urls.Count == 0) continue;
                    var srv = new RTCIceServer { urls = urls.ToArray() };
                    if (d.TryGetValue("username", out var un) && un != null) { srv.username = un.ToString(); srv.credentialType = RTCIceCredentialType.Password; }
                    if (d.TryGetValue("credential", out var cr) && cr != null) srv.credential = cr.ToString();
                    list.Add(srv);
                }
                VRLink.Log("webrtc_ice_from_web", list.Count);
                return list.ToArray();
            }
            if (!string.IsNullOrEmpty(link.stunUrl)) list.Add(new RTCIceServer { urls = new[] { link.stunUrl } });
            if (!string.IsNullOrEmpty(link.turnUrl))
                list.Add(new RTCIceServer
                {
                    urls = new[] { link.turnUrl },
                    username = link.turnUsername,
                    credential = link.turnCredential,
                    credentialType = RTCIceCredentialType.Password
                });
            return list.ToArray();
        }

        void StopSession()
        {
            IsStreaming = false;
            remoteSet = false;
            pendingRemote.Clear();
            track?.Dispose(); track = null;
            if (pc != null) { pc.Close(); pc.Dispose(); pc = null; }
            if (spectator != null) { spectator.targetTexture = null; spectator.enabled = false; }
        }

        void Fail(string step, string msg)
        {
            Debug.LogWarning($"[VRLink] WebRTC {step} 실패: {msg}");
            VRLink.Log("webrtc_error", new Dictionary<string, object> { ["step"] = step, ["message"] = msg });
            StopSession();
        }
#else
        void Start()
        {
            Debug.LogWarning("[VRLink] com.unity.webrtc 패키지가 없어 화면 스트리밍이 꺼져 있습니다. (파라미터·기록은 정상 동작)");
            VRLink.Log("webrtc_unavailable");
            enabled = false;
        }
#endif
    }
}
