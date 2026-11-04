using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FunBrainLab.VRLink.Editor
{
    /// <summary>
    /// 안드로이드(Vive Focus Vision / Quest 3) 빌드 설정 점검과 자동 수정.
    /// 메뉴: FunBrain ▸ VR Link ▸ 빌드 설정 점검
    /// </summary>
    public static class VRLinkBuildCheck
    {
        public readonly struct Issue
        {
            public readonly string Message;
            public readonly bool Required;
            public readonly Action Fix;
            public Issue(string message, bool required, Action fix) { Message = message; Required = required; Fix = fix; }
        }

        public static List<Issue> Collect()
        {
            var list = new List<Issue>();
            var android = NamedBuildTarget.Android;

            if (PlayerSettings.GetScriptingBackend(android) != ScriptingImplementation.IL2CPP)
                list.Add(new Issue("스크립팅 백엔드가 IL2CPP 가 아닙니다.", true,
                    () => PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP)));

            if (PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64)
                list.Add(new Issue("대상 아키텍처는 ARM64 만 켜야 합니다 (ARMv7 끄기).", true,
                    () => PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64));

            if (!PlayerSettings.Android.forceInternetPermission)
                list.Add(new Issue("Internet Access 를 Require 로 설정해야 합니다.", true,
                    () => PlayerSettings.Android.forceInternetPermission = true));

            if (PlayerSettings.Android.optimizedFramePacing)
                list.Add(new Issue("Optimized Frame Pacing 을 꺼야 합니다 (WebRTC 알려진 문제).", true,
                    () => PlayerSettings.Android.optimizedFramePacing = false));

            var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
            if (apis.Length > 0)
                list.Add(new Issue($"[정보] 그래픽 API: {string.Join(", ", apis)} — 어느 쪽이든 동작하도록 설계됨", false, null));

#if !VRLINK_WEBRTC
            list.Add(new Issue("[선택] com.unity.webrtc 패키지가 없습니다. 화면 스트리밍을 쓰려면 Package Manager ▸ Add package by name ▸ com.unity.webrtc", false, null));
#endif
            return list;
        }

        [MenuItem("FunBrain/VR Link/빌드 설정 점검", priority = 1)]
        public static void CheckFromMenu()
        {
            var issues = Collect();
            var required = issues.Where(i => i.Required).ToList();
            var text = issues.Count == 0
                ? "모든 설정이 올바릅니다."
                : string.Join("\n", issues.Select(i => (i.Required ? "• " : "  ") + i.Message));

            if (required.Count == 0)
            {
                EditorUtility.DisplayDialog("VR Link 빌드 설정", text.Length > 0 ? text : "모든 설정이 올바릅니다.", "확인");
                return;
            }
            if (EditorUtility.DisplayDialog("VR Link 빌드 설정", text + "\n\n필수 항목을 자동으로 고칠까요?", "자동 수정", "닫기"))
            {
                foreach (var i in required) i.Fix?.Invoke();
                AssetDatabase.SaveAssets();
                Debug.Log($"[VRLink] 빌드 설정 {required.Count}개 수정 완료");
            }
        }

        [MenuItem("FunBrain/VR Link/문서 열기", priority = 50)]
        static void OpenDocs()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VRLinkBuildCheck).Assembly);
            if (info != null) EditorUtility.RevealInFinder(System.IO.Path.Combine(info.resolvedPath, "README.md"));
        }

        sealed class PreBuild : IPreprocessBuildWithReport
        {
            public int callbackOrder => 0;

            public void OnPreprocessBuild(BuildReport report)
            {
                if (report.summary.platform != BuildTarget.Android) return;
                foreach (var i in Collect().Where(i => i.Required))
                    Debug.LogWarning($"[VRLink] {i.Message}  → 메뉴 FunBrain ▸ VR Link ▸ 빌드 설정 점검");
            }
        }
    }
}
