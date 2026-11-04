using UnityEditor;
using UnityEngine;

namespace FunBrainLab.VRLink.Editor
{
    static class VRLinkMenu
    {
        [MenuItem("GameObject/FunBrain/VR Link 추가", false, 10)]
        static void Create(MenuCommand cmd)
        {
            var existing = Object.FindAnyObjectByType<VRLink>(FindObjectsInactive.Include);
            if (existing != null)
            {
                Selection.activeObject = existing.gameObject;
                EditorGUIUtility.PingObject(existing.gameObject);
                Debug.Log("[VRLink] 씬에 이미 VR Link 가 있습니다.");
                return;
            }
            var go = new GameObject("VR Link");
            go.AddComponent<VRLink>();
            go.AddComponent<WebRtcStreamer>();
            Undo.RegisterCreatedObjectUndo(go, "VR Link 추가");
            Selection.activeObject = go;
        }

        [MenuItem("FunBrain/VR Link/씬에 VR Link 추가", priority = 0)]
        static void CreateFromTopMenu() => Create(null);
    }

    [CustomEditor(typeof(VRLink))]
    sealed class VRLinkInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            var link = (VRLink)target;
            if (Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    $"연결 코드: {(link.IsLinked ? link.PairingCode : "(입력 대기 — 헤드셋 키패드)")} [{link.pairingMode}]\n" +
                    $"기기: {link.Device}\n" +
                    $"Tunable {link.Registry.Tunables.Count} / Monitor {link.Registry.Monitors.Count} / Action {link.Registry.Actions.Count} | 네트워크: {(link.Network == null ? "꺼짐" : link.Network.Connected ? "연결됨" : "연결 중")}\n" +
                    $"WebRTC 패키지: {(VRLink.WebRtcAvailable ? "있음" : "없음")}",
                    MessageType.Info);
                if (GUILayout.Button("다시 수집")) link.Rescan();
                if (GUILayout.Button("연결 코드 초기화 (다음 실행 때 다시 묻기)")) VRLink.ResetPairingCode();
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "[Tunable(min, max)]  조절할 값 → 웹앱에 슬라이더 등 자동 생성\n" +
                    "[Monitor]            보여줄 상태 → 신호등·게이지 등 자동 생성\n" +
                    "[TunableAction]      실행할 동작 → 버튼 자동 생성\n" +
                    "VRLink.Log(\"이벤트\", 데이터)  수행 기록",
                    MessageType.None);
                if (GUILayout.Button("빌드 설정 점검")) VRLinkBuildCheck.CheckFromMenu();
                if (GUILayout.Button("이 PC(에디터)에 저장된 연결 코드 지우기")) VRLink.ResetPairingCode();
            }
        }
    }
}
