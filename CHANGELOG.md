# Changelog

## [0.4.1] - 2026-11-19
- 앱이 스스로 바꾼 조절값(PublishValues)을 control/params 에도 반영 — 웹에서 예전 값으로 되돌릴 때 이벤트가 나지 않아 적용되지 않던 문제 (웹앱 쪽에도 같은 취지의 보완이 있어 구버전 앱도 동작)

## [0.4.0] - 2026-11-05
- 메뉴 FunBrain → VR Link → "협력업체용 DLL 패키지 내보내기…": Runtime 소스를 FunBrainLab.VRLink.dll 로 컴파일하고 Firebase 주소·연결 코드 방식·STUN 을 DLL 에 고정(VRLINK_PARTNER). 받는 쪽 인스펙터에는 설정이 보이지 않음

## [0.3.3] - 2026-11-04
- 컴파일 오류 수정: VRLink.cs 의 Registry.TryGet → TryGetTunable (실제 Unity 첫 컴파일 보고 반영)

## [0.3.2] - 2026-11-04
- 웹앱이 request 에 실어 보낸 iceServers(서버 발급 TURN 자격증명)를 우선 사용 — 헤드셋에 TURN 비밀을 두지 않음

## [0.3.1] - 2026-11-04
- WebRTC 시그널에 session 추가 — 다른 창·PC 의 응답이 섞여 연결이 실패하던 문제

## [0.3.0] - 2026-11-04
- 연결 코드 흐름: PairingMode (AskOnFirstRun 기본 / AutoFromDevice / Fixed), 헤드셋 안 키패드(포인터 + 시선 dwell), USB 주입(inbox/pairing.json), 기기 ID 기반 코드, ResetPairingCode()
- 코드가 정해질 때까지 통로를 열지 않음 (IsLinked)
- asmdef 에 UnityEngine.UI 참조 추가

## [0.2.0] - 2026-11-04
- [Monitor] 상태 표시 (number/bar/ring/dial/sparkline/timer/trafficlight/led/badge/text/xy), 변수별 Interval 로 바뀐 값만 전송
- [TunableAction] 버튼 동작 (button/primary/danger, Confirm), 요청번호로 중복 실행 방지
- [Tunable] 에 Widget/Unit/Order 추가 (slider/stepper/number/knob/toggle/checkbox/dropdown/buttons/radio/text/textarea/color)
- 스키마 v2: {controls, monitors, actions}
- Probe 샘플에 가짜 과제(시작/일시정지/초기화) 추가

## [0.1.0] - 2026-11-04
- 첫 뼈대: [Tunable] 수집·적용, USB(ADB 파일)/RTDB 통로, 기기 자동 감지, 수행 기록, WebRTC 관찰 카메라 스트리밍, 빌드 설정 점검, Probe 검증 샘플
- 실기기 미검증
