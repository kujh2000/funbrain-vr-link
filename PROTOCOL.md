# VR Link 데이터 규격 (웹앱 개발용) — v0.2

웹앱(프로젝트 관리 + 대시보드 + 화면 보기 + APK 설치)과 Unity 패키지가 공유하는 규격입니다.
같은 JSON을 **네트워크(RTDB)** 와 **USB(ADB 파일)** 두 통로에서 똑같이 씁니다.

## 1. 연결(페어링) 코드

6자, 문자 집합 `ABCDEFGHJKLMNPQRSTUVWXYZ23456789` (I, O, 0, 1 제외). 헤드셋 앱은 어느 프로젝트에 속하는지 모르며, 프로젝트↔코드 연결은 웹앱(Firestore)에만 있습니다.

코드가 정해지는 두 방향:
| 흐름 | 누가 만드나 | 쓰임 |
|---|---|---|
| **발급** (웹→헤드셋) | 웹앱 "기기 추가 → 코드 발급". QR 내용은 `vrlink:K7M2QX` | 병원·외부 기관. 헤드셋은 첫 실행 키패드(또는 USB `inbox/pairing.json`)로 입력 |
| **등록** (헤드셋→웹) | 헤드셋이 기기 ID 해시로 생성(AutoFromDevice) 또는 Fixed | 연구실 시험. 헤드셋 화면의 코드를 웹앱에 등록 |

헤드셋 쪽 결정 순서: Fixed → USB 주입 파일 → 저장된 코드 → (Auto면 기기 ID 해시 / Ask면 키패드). 저장된 코드는 `VRLink.ResetPairingCode()`로 지웁니다.

## 2. 경로

| 데이터 | RTDB 경로 (`vrlink/devices/{code}/`) | ADB 파일 (`/sdcard/Android/data/{pkg}/files/vrlink/`) | 방향 | 방식 |
|---|---|---|---|---|
| 앱·기기 정보 | `hello` | `state/hello.json` | 앱→웹 | 덮어쓰기 |
| 스키마 (조절·상태·동작 목록) | `schema` | `state/schema.json` | 앱→웹 | 덮어쓰기 |
| 조절값 현재값 | `values` | `state/values.json` | 앱→웹 | 덮어쓰기 |
| 상태값 | `monitors/{id}` | `state/monitors.json` | 앱→웹 | 바뀐 것만 PATCH (변수별 Interval) |
| 상태 (5초) | `status` | `state/status.json` | 앱→웹 | 덮어쓰기 |
| 수행 기록 | `logs/{pushId}` | `log.jsonl` (한 줄 하나) | 앱→웹 | 추가 |
| 조절값 변경 | `control/params/{id}` | `inbox/params.json` | 웹→앱 | 웹이 PATCH (ADB는 앱이 읽고 삭제) |
| 동작 실행 | `control/actions/{id}` = 요청번호 | `inbox/actions.json` | 웹→앱 | 웹이 쓰기. 요청번호(nonce)가 바뀔 때만 실행 |
| 연결 코드 주입 | — | `inbox/pairing.json` `{"code":"K7M2QX"}` | PC→앱 | 앱 첫 실행 전 USB로 push. 읽으면 저장 후 삭제 |
| WebRTC 시그널 | `signal/toDevice/{pushId}` | — | 웹→앱 | 추가 |
| WebRTC 시그널 | `signal/toWeb/{pushId}` | — | 앱→웹 | 추가 |

## 3. 스키마

```json
{
  "version": 2,
  "controls": [ ...조절 항목... ],
  "monitors": [ ...상태 항목... ],
  "actions":  [ ...동작 항목... ]
}
```

모든 항목 공통 필드: `id`, `label`, `group`, `order`, `kind`, `widget`, `help`(선택)
`id` 형식: `{오브젝트}_{스크립트}_{변수}` 를 `-`로 이은 것 (Firebase 금지 문자와 `-`는 `_`로 치환)

### controls (웹 → 앱)
```json
{"id":"Task-ReachTask-difficulty","label":"난이도","group":"과제","order":1,
 "kind":"int","widget":"slider","min":1,"max":5,"step":1,"unit":"","value":3}
{"id":"Task-ReachTask-hand","label":"사용 손","group":"과제","order":4,
 "kind":"enum","widget":"buttons","options":["Left","Right","Both"],"value":"Right"}
```
`kind`: `int` · `float` · `bool` · `string` · `enum`. `min`/`max`가 있으면 앱이 범위를 강제(clamp)합니다.

### monitors (앱 → 웹)
```json
{"id":"Task-ReachTask-phase","label":"진행 단계","group":"상태","order":1,
 "kind":"enum","widget":"trafficlight","options":["Idle","Running","Paused","Done"],
 "colors":["gray","green","yellow","blue"],"interval":0.5,"decimals":2,"value":"Idle"}
{"id":"Task-ReachTask-progress","label":"진행률","group":"상태","order":2,
 "kind":"float","widget":"ring","min":0,"max":100,"unit":"%","decimals":0,"interval":0.5,"value":0}
{"id":"Task-ReachTask-slowFrames","label":"느린 프레임","group":"성능",
 "kind":"int","widget":"trafficlight","thresholds":[1,5],"invert":false,"value":0}
{"id":"Task-ReachTask-gazeXY","label":"시선 방향","group":"상태",
 "kind":"vector2","widget":"xy","min":-1,"max":1,"value":{"x":0.1,"y":-0.2}}
```
`kind`: `int` · `float` · `bool` · `string` · `enum` · `vector2` · `vector3`
- `colors`: enum 선언 순서대로의 색 이름 (`green` `yellow` `red` `gray` `blue`)
- `thresholds`: 숫자 경계 `[a, b]` → `<a` green, `<b` yellow, 그 이상 red. `invert:true`면 반대
- `interval`: 앱이 이 변수를 확인·전송하는 최소 간격(초)

### actions (웹 → 앱)
```json
{"id":"Task-ReachTask-StartRun","label":"시작","group":"제어","order":1,"kind":"action","widget":"primary"}
{"id":"Task-ReachTask-ResetRun","label":"초기화","group":"제어","kind":"action","widget":"danger","confirm":true}
```
웹앱은 `control/actions/{id}`에 새 요청번호(예: 타임스탬프나 UUID)를 씁니다. 앱은 번호가 바뀔 때만 실행하고 `action` 기록을 남깁니다.

## 4. 위젯 카탈로그 (웹앱이 구현해야 할 목록)

Unity 코드의 `Widget` 값이 **기본값**이고, 웹앱 편집 모드에서 사용자가 바꾼 값은 프로젝트(또는 개인) 레이아웃에 저장되어 기본값을 덮어씁니다. 아래 표는 기본값(★)과 허용되는 위젯입니다.

### 조절 (controls)
| kind | 위젯 | 설명 |
|---|---|---|
| int, float | **slider**★ | 가로 슬라이더 + 현재값. `step` 반영 |
| | stepper | − 값 + 버튼. 정수에 적합 |
| | number | 숫자 입력란 |
| | knob | 원형 손잡이 (각도·속도 등) |
| bool | **toggle**★ | 스위치 |
| | checkbox | 체크 박스 |
| enum | **dropdown**★ | 드롭다운 |
| | buttons | 가로 버튼 묶음 (segmented) |
| | radio | 라디오 버튼 |
| string | **text**★ | 한 줄 입력 |
| | textarea | 여러 줄 입력 |
| | color | 색 선택 (`#RRGGBB`) |

### 상태 (monitors)
| kind | 위젯 | 설명 |
|---|---|---|
| int, float | **number**★ | 큰 숫자 타일 + 단위 |
| | bar | 가로 프로그레스 바 (`min`~`max`) |
| | ring | 원형 프로그레스 (도넛) |
| | dial | 반원 게이지 (계기판) |
| | sparkline | 최근 60초 추이 선 그래프 |
| | timer | 초 → `mm:ss` 표시 |
| | trafficlight | `thresholds`로 초록/노랑/빨강 |
| bool | **led**★ | 켜짐/꺼짐 램프 |
| | badge | 참/거짓 문구 배지 |
| enum | **badge**★ | 현재 값 문구 배지 |
| | trafficlight | `colors`로 enum마다 색 |
| string | **text**★ | 문구 |
| | badge | 배지 |
| vector2, vector3 | **xy**★ | 사각 영역 안의 점 (x, y). vector3는 z 무시 |
| | text | `(x, y, z)` 문구 |

### 동작 (actions)
| 위젯 | 설명 |
|---|---|
| **button**★ | 보통 버튼 |
| primary | 강조 버튼 (시작 등) |
| danger | 빨간 버튼 (초기화 등). `confirm:true`면 확인 창 |

### 대시보드 고정 위젯 (스키마와 무관하게 항상 제공)
| 위젯 | 내용 |
|---|---|
| 화면 보기 | WebRTC 스트리밍 |
| 기기 상태 | `status` (FPS, 배터리, 발열, 씬) |
| 기록 | `logs` 실시간 표 + CSV/JSON 내보내기 |
| 프리셋 | 조절값 묶음 저장·불러오기 |

## 5. 그 외 JSON

### hello
```json
{"code":"K7M2QX","app":"kr.funbrainlab.reach","appVersion":"1.2.0","productName":"Reach",
 "unity":"6000.4.4f1","vrlink":"0.2.0","webrtc":true,
 "device":{"kind":"ViveFocusVision","manufacturer":"HTC","model":"...","xrDevice":"...",
           "graphicsApi":"Vulkan","stereoMode":"SinglePassInstanced","eyeWidth":2448,"eyeHeight":2448,
           "refreshRate":90,"os":"Android OS 12"},
 "startedAt":"2026-11-04T01:23:45.000Z"}
```
`device.kind`: `ViveFocusVision` · `ViveOther` · `Quest3` · `Quest3S` · `MetaOther` · `Editor` · `Unknown`

### values / control/params
```json
{"Task-ReachTask-difficulty": 4, "Task-ReachTask-hand": "Both"}
```

### status
```json
{"t":"...","fps":89.8,"slowFrames":0,"targetHz":90,"scene":"Main","battery":0.82,"thermal":"0"}
```

### logs (한 건)
```json
{"t":"2026-11-04T01:25:00.000Z","ts":135.42,"event":"trial_end","scene":"Main","data":{"score":8,"rt":0.42}}
```
패키지 기본 이벤트: `param_applied`, `action`, `action_failed`, `action_unknown`, `probe_fps`, `webrtc_offer_sent`, `webrtc_ice`, `webrtc_error`, `webrtc_stopped`, `webrtc_unavailable`

## 6. WebRTC 시그널링 순서

```
웹앱                                              헤드셋(앱)
 │ toDevice ← {type:"request", session, iceServers?} ─▶ 관찰 카메라 시작, offer 생성
 │                                        ◀───   toWeb ← {type:"offer", sdp, width, height, device, session}
 │ toDevice ← {type:"answer", sdp, session} ─▶
 │ toDevice ← {type:"candidate", candidate, sdpMid, sdpMLineIndex, session}  (양방향)
 │                                        ◀───   toWeb ← {type:"candidate", ..., session}
 │ toDevice ← {type:"stop", session}      ───▶   세션 종료
```
- `iceServers`: `[{urls:[…], username?, credential?}]`. 웹앱이 서버(/api/turn-credentials)에서 받은 2시간짜리 TURN 자격증명을 포함해 보낸다. 헤드셋은 있으면 이것을 우선 쓰고 저장하지 않는다. 없으면 Inspector 의 STUN/TURN 설정.
- `session`: 웹앱이 request 마다 새로 만드는 문자열.

### 조절값 되돌리기 주의
RTDB 는 값이 실제로 바뀔 때만 이벤트를 보낸다. 앱이 스스로 값을 바꾸면(`PublishValues`) `values` 와 함께 `control/params` 도 갱신해 두고(v0.4.1+), 웹앱은 저장값과 같은 키를 보낼 때 먼저 비웠다가(null, 문자열은 보이지 않는 문자 추가) 다시 쓴다. 웹앱이 `control/params` 에 쓰는 `_t`(전송 시각) 키는 무시한다. 헤드셋은 request 의 session 을 기억하고 그 밖의 메시지는 session 이 같을 때만 처리하며, 웹앱도 자기 session 의 offer/candidate 만 받는다. 여러 창·PC 가 같은 기기를 열어 두어도 응답이 섞이지 않는다.
- 영상 자체는 Firebase를 거치지 않고 P2P(또는 TURN 중계)로만 흐릅니다. Firebase에는 위 시그널 메시지만 지나갑니다.
- 헤드셋이 offer를 만들고(송신), 웹앱은 수신 전용(`recvonly`)으로 answer합니다.
- 새 세션 시작 시 앱이 `signal/toWeb`을 비웁니다. 웹앱도 request 전에 `signal/toDevice`를 비우세요.
- ICE 서버(STUN/TURN)는 웹앱과 앱이 같은 값을 써야 합니다.

## 7. 웹앱 데이터 (Firestore, 헤드셋은 모름)

```
projects/{projectId}
  name, ownerUid, members: {uid: "owner"|"editor"|"viewer"}, createdAt
  devices: {code: {label, kind, addedAt}}
  layout: {widgets: [{id, widget, x, y, w, h}], sharedLayout: true}
  presets/{presetId}: {name, values: {id: 값}, createdBy}
  apks/{apkId}: {name, version, packageName, storagePath, sha256, size}
users/{uid}/layouts/{projectId}: 개인 레이아웃 (sharedLayout=false 일 때)
```

## 8. 보안 (운영 전 필수)

- 개발용: `vrlink/devices/$code` 읽기/쓰기 허용 (코드를 아는 사람만 접근)
- 운영용(예정): 웹앱이 로그인한 사용자에게만 페어링 토큰 발급 → 헤드셋은 그 토큰으로 접근, 세션 종료 시 폐기
- 화면에 환자 식별 정보를 표시하지 마세요. 영상은 서버에 저장되지 않습니다.
