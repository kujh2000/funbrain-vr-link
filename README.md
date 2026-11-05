# FunBrain VR Link (v0.3.0, 뼈대)

학생들이 만드는 Unity VR 재활 앱을 연구실 웹앱과 연결하는 패키지입니다.
**Unity 코드에 속성만 붙이면 웹앱 화면이 그 프로젝트에 맞게 자동으로 만들어집니다.** 웹앱은 건드리지 않습니다.

- **조절** `[Tunable]`: 웹앱에 슬라이더·스테퍼·스위치·드롭다운 등이 자동 생성 (웹 → 앱)
- **상태** `[Monitor]`: 신호등·게이지(바/링/다이얼)·숫자·그래프·LED 등으로 표시 (앱 → 웹)
- **동작** `[TunableAction]`: 버튼 자동 생성 (웹 → 앱)
- **수행 기록 전송**: `VRLink.Log("이벤트", 데이터)` 한 줄로 기록이 웹앱에 쌓입니다.
- **화면 스트리밍**: WebRTC로 헤드셋 화면을 웹앱에서 봅니다.
- **두 가지 통로 동시 사용**: USB(ADB 파일)와 네트워크(Firebase Realtime Database).

대상 기기: **Vive Focus Vision**, **Meta Quest 3** (OpenXR, Unity 6, URP)

> ⚠️ 아직 실기기에서 빌드·검증하지 않은 뼈대입니다. 아래 "0단계 검증"부터 진행하세요.

---

## 1. 설치

Unity ▸ Window ▸ Package Manager ▸ **+** ▸ *Install package from git URL...*

```
https://github.com/kujh2000/funbrain-vr-link.git
```

(저장소에 올리기 전에는 *Install package from disk...* 로 이 폴더의 `package.json`을 선택)

화면 스트리밍을 쓰려면 같은 방법으로 **Add package by name** ▸ `com.unity.webrtc` 도 추가합니다.
없어도 컴파일 오류 없이 파라미터·기록 기능은 동작합니다.

## 2. 사용법 (학생용)

1. 메뉴 **GameObject ▸ FunBrain ▸ VR Link 추가** → 씬에 `VR Link` 오브젝트가 생깁니다.
2. Inspector에서 `Database Url` 입력 (연구실 공통 값, 아래 6절), `Pairing Mode` 선택
   - **AskOnFirstRun** (기본, 병원·외부 기관): 앱을 처음 켜면 헤드셋 안에 키패드가 떠서 웹앱에서 발급한 6자리 코드를 입력합니다. 한 번 입력하면 저장됩니다. 컨트롤러 포인터 또는 **시선을 1초 머무르기**로 누릅니다.
   - **AutoFromDevice** (연구실 시험): 기기 ID로 코드를 만듭니다(재설치해도 동일). 헤드셋 화면의 코드를 웹앱에 등록합니다.
   - **Fixed**: 기기 한 대 전용 빌드일 때만. `Fixed Code`에 직접 입력.
   - USB로 설치할 때 코드를 미리 넣을 수도 있습니다 (7절 `pairing.json`). 그러면 키패드가 뜨지 않습니다.
   - 코드를 바꾸려면 코드에서 `VRLink.ResetPairingCode()` 호출(또는 Inspector 버튼) 후 앱 재시작.
3. 메뉴 **FunBrain ▸ VR Link ▸ 빌드 설정 점검** → "자동 수정"
4. 코드에 속성 붙이기:

```csharp
using FunBrainLab.VRLink;

public class ReachTask : MonoBehaviour
{
    // ---- 조절 (웹 → 앱) ----
    [Tunable(1, 5, Label = "난이도", Group = "과제")]
    public int difficulty = 3;

    [Tunable(0.5f, 2f, Label = "목표 속도", Group = "과제", Step = 0.1f, Unit = "m/s")]
    public float speed = 1f;

    [Tunable(Label = "보조 화살표")]
    public bool showHint = true;

    public enum Hand { Left, Right, Both }
    [Tunable(Label = "사용 손", Widget = "buttons")]
    public Hand hand = Hand.Right;

    // ---- 상태 (앱 → 웹) ----
    public enum Phase { Idle, Running, Done }
    [Monitor(Label = "진행 단계", Widget = "trafficlight", Colors = "gray,green,blue")]
    public Phase phase;

    [Monitor(Label = "진행률", Widget = "ring", Min = 0, Max = 100, Unit = "%", Decimals = 0)]
    public float progress;

    [Monitor(Label = "성공 횟수", Unit = "회", Decimals = 0)]
    public int successCount;

    [Monitor(Label = "반응시간", Widget = "sparkline", Min = 0, Max = 2, Unit = "s")]
    public float lastRt;

    [Monitor(Label = "손 감지", Widget = "led")]
    public bool handDetected;

    // ---- 동작 (웹 → 앱) ----
    [TunableAction(Label = "과제 시작", Group = "제어", Widget = "primary")]
    void StartTask() { phase = Phase.Running; }

    [TunableAction(Label = "초기화", Group = "제어", Widget = "danger", Confirm = true)]
    void ResetTask() { phase = Phase.Idle; progress = 0; successCount = 0; }

    void OnTrialEnd(int score, float rt)
    {
        successCount++; lastRt = rt;
        VRLink.Log("trial_end", new Dictionary<string, object> { ["score"] = score, ["rt"] = rt });
    }
}
```

**위젯 종류** (전체 표는 PROTOCOL.md 4절)
- 조절: `slider` `stepper` `number` `knob` / `toggle` `checkbox` / `dropdown` `buttons` `radio` / `text` `textarea` `color`
- 상태: `number` `bar` `ring` `dial` `sparkline` `timer` `trafficlight` / `led` `badge` / `text` / `xy`(Vector2·3)
- 동작: `button` `primary` `danger`

`Widget`을 비우면 타입 기본값이 쓰입니다. 웹앱 편집 모드에서 사용자가 위젯·배치를 바꾸면 그 설정이 프로젝트에 저장되므로 Unity를 다시 빌드할 필요가 없습니다.

값이 바뀐 순간에 처리가 필요하면:

```csharp
void OnEnable()  => VRLink.ParamChanged += OnParam;
void OnDisable() => VRLink.ParamChanged -= OnParam;
void OnParam(string id, object value) { /* 예: 난이도 바뀌면 목표 재배치 */ }
```

지원 타입: `int`, `float`, `double`, `bool`, `string`, `enum` (`[Monitor]`는 `Vector2`, `Vector3`도). 필드 또는 속성.
`[TunableAction]`은 매개변수 없는 메서드(private 가능)에 붙입니다.
런타임에 오브젝트를 새로 만들었다면 `VRLink.Instance.Rescan()` 호출.

## 3. 다양한 환경 대응 방식

| 차이 | 대응 |
|---|---|
| 헤드셋 (Vive / Quest) | 실행 시 `DeviceInfo`가 자동 감지해 웹앱에 보고 |
| 그래픽 API (Vulkan / GLES) | XR 눈 텍스처를 직접 읽지 않으므로 무관 |
| 스테레오 방식 (Multi-pass / SPI / Multiview) | 머리를 따라다니는 **모노 관찰 카메라**를 따로 렌더링 → 무관 |
| WebRTC 패키지 유무 | `VRLINK_WEBRTC` 자동 정의, 없으면 스트리밍만 꺼짐 |
| 네트워크 없음 | USB(ADB 파일) 통로만으로 파라미터·기록 동작 |

관찰 카메라 방식은 GPU를 조금 더 씁니다. 0단계 검증에서 `loadLevel`을 올려 가며 프레임 유지를 확인하고 `WebRtcStreamer`의 해상도·FPS를 조정하세요.

**프로젝트 권장 설정**: 하나의 Unity 프로젝트에 Build Profile 두 개 (Vive용: VIVE OpenXR 기능 그룹, Quest용: Meta Quest 기능). 구형 `com.unity.xr.oculus` 플러그인은 제거하고 OpenXR로 통일합니다.

## 4. 0단계 실기기 검증 (Vive Focus Vision, Quest 3 각각)

1. Package Manager ▸ FunBrain VR Link ▸ Samples ▸ **Probe** Import
2. 빈 VR 씬에 `VR Link` 추가 + 빈 오브젝트에 `VRLinkProbe` 추가
3. 빌드 설정 점검 → 빌드 → 설치
4. 처음 실행이면 키패드에 웹앱에서 발급한 코드 입력 (시험용이면 Inspector 에서 AutoFromDevice 로 바꿔도 됨)
5. 헤드셋 앞 패널에서 확인: `CODE`, fps / Hz, 기기 정보, net, webrtc 상태

| 항목 | 확인 방법 | 기록 |
|---|---|---|
| 기기 감지 | 패널 3번째 줄 (Kind, 그래픽 API, 스테레오 방식) | |
| ADB 파라미터 | 아래 7절 명령으로 params.json push → 큐브 변화 | |
| 네트워크 파라미터 | 웹앱 슬라이더(또는 RTDB 콘솔 `control/params`) → 큐브 변화 | |
| 상태 위젯 | 웹앱에서 "시작" 버튼 → 신호등·진행률·성공 횟수 변화 | |
| WebRTC 빌드 | 6000.4에서 빌드 성공 여부 (안 되면 6000.3 검토) | |
| 스트리밍 지연 | 웹앱 화면 보기 + 큐브 회전 비교 | |
| 프레임 유지 | 스트리밍 중 loadLevel 0→10, slow 값 관찰 | |
| 외부망(TURN) | 휴대폰 핫스팟 등 다른 망에서 연결 | |

## 5. 폴더 구조

```
Runtime/
  VRLink.cs              연결 관리자 (페어링 코드, 수집, 기록, 상태, 동작 실행)
  Attributes.cs          [Tunable] [Monitor] [TunableAction]
  TunableRegistry.cs     수집·스키마·값 적용·상태 변경 감지
  DeviceInfo.cs          헤드셋·렌더링 환경 감지
  WebRtcStreamer.cs      화면 스트리밍 (관찰 카메라 + WebRTC)
  Channels/              AdbFileChannel(USB), RtdbChannel(네트워크)
  Pairing/               PairingStore(코드 결정·저장), PairingKeypad(헤드셋 안 코드 입력 화면)
  Json.cs, FrameStats.cs
Editor/                  빌드 설정 점검, 메뉴, Inspector
Samples~/Probe/          0단계 검증용
PROTOCOL.md              웹앱 개발용 데이터 규격
```

## 6. 연구실 공통 설정 (준비 필요)

- **Firebase Realtime Database**: `funbrainlab-apps` 프로젝트에 RTDB를 추가하고 URL을 `Database Url`에 넣습니다.
- **보안 규칙**: 개발 중에는 `vrlink/` 경로만 열어 두고, 병원 운영 전에는 페어링 시 웹앱이 발급한 토큰으로 제한합니다 (PROTOCOL.md 참고).
- **TURN 서버**: 병원망 대비. NAS의 coturn 또는 유료 TURN 서비스 → `Turn Url / Username / Credential`.

## 7. USB(ADB)로 직접 시험하기

```bash
# 앱이 보낸 목록·상태 보기
adb shell cat /sdcard/Android/data/<패키지명>/files/vrlink/state/schema.json
adb shell cat /sdcard/Android/data/<패키지명>/files/vrlink/state/status.json

# 파라미터 보내기 (id는 schema.json 참고)
echo '{"Probe-VRLinkProbe-cubeScale":0.8,"Probe-VRLinkProbe-color":"Cyan"}' > params.json
adb push params.json /sdcard/Android/data/<패키지명>/files/vrlink/inbox/params.json

# 연결 코드 미리 넣기 (앱 첫 실행 전에. 웹앱 '코드 발급'에서 받은 값)
adb shell mkdir -p /sdcard/Android/data/<패키지명>/files/vrlink/inbox
echo '{"code":"K7M2QX"}' > pairing.json
adb push pairing.json /sdcard/Android/data/<패키지명>/files/vrlink/inbox/pairing.json

# 동작 실행 (값은 아무 새 문자열이나 — 바뀔 때만 실행됨)
echo '{"Probe-VRLinkProbe-StartRun":"1"}' > actions.json
adb push actions.json /sdcard/Android/data/<패키지명>/files/vrlink/inbox/actions.json

# 상태값 보기
adb shell cat /sdcard/Android/data/<패키지명>/files/vrlink/state/monitors.json

# 기록 가져오기
adb pull /sdcard/Android/data/<패키지명>/files/vrlink/log.jsonl
```

## 협력기관에 프로젝트를 넘길 때 (DLL 배포판)
Unity 메뉴 **FunBrain → VR Link → 협력업체용 DLL 패키지 내보내기…** 를 열고 Realtime Database URL·연결 코드 방식·출력 폴더를 지정해 "DLL 패키지 만들기"를 누르면
`com.funbrainlab.vrlink/` 폴더(Runtime/FunBrainLab.VRLink.dll + package.json + README + LICENSE)가 만들어진다.
- 상대 프로젝트의 `Packages/` 아래에 이 폴더를 복사한다. `[Tunable]`·`[Monitor]`·`[TunableAction]`·`VRLink`·`WebRtcStreamer` 는 그대로 쓸 수 있다.
- 설정값은 DLL 안에 고정되어 인스펙터에 나타나지 않는다. 내보낸 프로젝트에 `com.unity.webrtc` 가 있었으면 받는 쪽에도 같은 패키지가 필요하다.
- 소스를 숨길 뿐 완전한 보호는 아니다(디컴파일 가능). 비밀유지·재배포 금지 조항과 함께 쓴다. 이 저장소 자체는 비공개로 두는 것을 권장.
