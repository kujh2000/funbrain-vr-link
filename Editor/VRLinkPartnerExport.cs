using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace FunBrainLab.VRLink.Editor
{
    /// <summary>
    /// 협력업체(병원·외부 개발사)에 Unity 프로젝트를 넘길 때 쓰는 "DLL 패키지"를 만든다.
    /// Runtime 소스를 Unity 가 직접 컴파일해 FunBrainLab.VRLink.dll 하나로 묶고,
    /// Firebase 주소·연결 코드 방식·STUN 같은 설정은 DLL 안에 고정해 인스펙터에 보이지 않게 한다.
    /// 결과 폴더를 상대 프로젝트의 Packages/com.funbrainlab.vrlink/ 로 복사하면 된다.
    /// </summary>
    sealed class VRLinkPartnerExport : EditorWindow
    {
        string databaseUrl = "";
        PairingMode pairingMode = PairingMode.AskOnFirstRun;
        string stunUrl = "stun:stun.l.google.com:19302";
        string outputDir = "";
        string status = "";
        bool building;

        [MenuItem("FunBrain/VR Link/협력업체용 DLL 패키지 내보내기…", priority = 50)]
        static void Open()
        {
            var w = GetWindow<VRLinkPartnerExport>(true, "VR Link 협력업체용 패키지");
            w.minSize = new Vector2(520, 300);
            var link = UnityEngine.Object.FindAnyObjectByType<VRLink>(FindObjectsInactive.Include);
            if (link != null)
            {
                w.databaseUrl = link.databaseUrl;
                w.pairingMode = link.pairingMode;
                w.stunUrl = link.stunUrl;
            }
            w.databaseUrl = EditorPrefs.GetString("VRLink.Partner.databaseUrl", w.databaseUrl);
            w.outputDir = EditorPrefs.GetString("VRLink.Partner.outputDir", Path.Combine(Directory.GetParent(Application.dataPath).FullName, "VRLink-partner"));
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox("소스 대신 DLL 만 들어 있는 패키지를 만듭니다. 받는 쪽은 VR Link 컴포넌트와 [Tunable]/[Monitor]/[TunableAction] 만 보이고, 아래 설정은 DLL 에 고정되어 인스펙터에 나타나지 않습니다.\n완전한 보호는 아닙니다(디컴파일 가능) — 비밀유지 계약과 함께 쓰세요.", MessageType.Info);
            databaseUrl = EditorGUILayout.TextField("Realtime Database URL", databaseUrl);
            pairingMode = (PairingMode)EditorGUILayout.EnumPopup("연결 코드 방식", pairingMode);
            stunUrl = EditorGUILayout.TextField("STUN", stunUrl);
            using (new EditorGUILayout.HorizontalScope())
            {
                outputDir = EditorGUILayout.TextField("출력 폴더", outputDir);
                if (GUILayout.Button("…", GUILayout.Width(28)))
                {
                    var d = EditorUtility.OpenFolderPanel("출력 폴더", outputDir, "");
                    if (!string.IsNullOrEmpty(d)) outputDir = d;
                }
            }
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(building || string.IsNullOrEmpty(databaseUrl) || string.IsNullOrEmpty(outputDir)))
            {
                if (GUILayout.Button("DLL 패키지 만들기", GUILayout.Height(32))) Build();
            }
            if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, building ? MessageType.Info : MessageType.None);
        }

        static string PackageRoot()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VRLink).Assembly);
            if (info == null) throw new Exception("VR Link 패키지를 찾을 수 없습니다 (소스 패키지에서만 내보낼 수 있습니다)");
            return info.resolvedPath;
        }

        void Build()
        {
            EditorPrefs.SetString("VRLink.Partner.databaseUrl", databaseUrl);
            EditorPrefs.SetString("VRLink.Partner.outputDir", outputDir);
            building = true;
            status = "컴파일 중…";
            try
            {
                var root = PackageRoot();
                var pkg = JsonUtility.FromJson<PkgJson>(File.ReadAllText(Path.Combine(root, "package.json")));
                var temp = Path.Combine(Path.GetTempPath(), "vrlink-partner-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temp);

                // 1) Runtime 소스 복사 + 고정 설정 파일 생성
                var scripts = new List<string>();
                foreach (var f in Directory.GetFiles(Path.Combine(root, "Runtime"), "*.cs", SearchOption.AllDirectories))
                {
                    var dst = Path.Combine(temp, Path.GetFileName(f));
                    File.Copy(f, dst, true);
                    scripts.Add(dst);
                }
                var cfg = Path.Combine(temp, "PartnerConfig.cs");
                File.WriteAllText(cfg, GenerateConfig());
                scripts.Add(cfg);

                // 2) 출력 패키지 폴더
                var outPkg = Path.Combine(outputDir, "com.funbrainlab.vrlink");
                var outRuntime = Path.Combine(outPkg, "Runtime");
                Directory.CreateDirectory(outRuntime);
                var dll = Path.Combine(outRuntime, "FunBrainLab.VRLink.dll");

                // 3) Unity 의 컴파일러로 DLL 생성 (엔진 참조는 자동, WebRTC·UI 는 현재 프로젝트의 것을 참조)
                var builder = new AssemblyBuilder(dll, scripts.ToArray())
                {
                    buildTarget = BuildTarget.Android,
                    buildTargetGroup = BuildTargetGroup.Android,
                    referencesOptions = ReferencesOptions.UseEngineModules,
                };
                var defines = new List<string> { "VRLINK_PARTNER" };
                var refs = new List<string>();
                foreach (var a in CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies))
                {
                    if (a.name == "Unity.WebRTC") { refs.Add(a.outputPath); defines.Add("VRLINK_WEBRTC"); }
                    if (a.name == "UnityEngine.UI") refs.Add(a.outputPath);
                }
                builder.additionalDefines = defines.ToArray();
                builder.additionalReferences = refs.ToArray();
                builder.buildFinished += (path, messages) =>
                {
                    building = false;
                    var errors = messages.Where(m => m.type == CompilerMessageType.Error).ToList();
                    if (errors.Count > 0)
                    {
                        status = "컴파일 오류 " + errors.Count + "개:\n" + string.Join("\n", errors.Take(5).Select(e => e.message));
                        foreach (var e in errors) Debug.LogError("[VRLink] " + e.message);
                        return;
                    }
                    WritePackageFiles(outPkg, pkg.version, defines.Contains("VRLINK_WEBRTC"));
                    try { Directory.Delete(temp, true); } catch { /* 무시 */ }
                    status = "완료: " + outPkg + "\n상대 프로젝트의 Packages/ 아래에 이 폴더를 통째로 복사하세요.";
                    EditorUtility.RevealInFinder(outPkg);
                    Repaint();
                };
                if (!builder.Build()) { building = false; status = "컴파일을 시작할 수 없습니다 (다른 컴파일이 진행 중?)"; }
            }
            catch (Exception e)
            {
                building = false;
                status = "실패: " + e.Message;
                Debug.LogException(e);
            }
        }

        string GenerateConfig()
        {
            string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
            return
"namespace FunBrainLab.VRLink\n{\n" +
"    /// <summary>협력업체용 빌드에 고정된 설정 (내보내기 때 생성됨)</summary>\n" +
"    internal static class PartnerConfig\n    {\n" +
"        internal static void Apply(VRLink l)\n        {\n" +
"            l.databaseUrl = \"" + Esc(databaseUrl) + "\";\n" +
"            l.pairingMode = PairingMode." + pairingMode + ";\n" +
"            l.stunUrl = \"" + Esc(stunUrl) + "\";\n" +
"            l.useNetwork = true;\n" +
"        }\n    }\n}\n";
        }

        static void WritePackageFiles(string outPkg, string version, bool webrtc)
        {
            File.WriteAllText(Path.Combine(outPkg, "package.json"),
"{\n  \"name\": \"com.funbrainlab.vrlink\",\n  \"version\": \"" + version + "\",\n  \"displayName\": \"FunBrain VR Link (partner)\",\n" +
"  \"description\": \"FunBrainLab VR Link — 협력기관용 배포판. 소스 없이 DLL 만 포함.\",\n  \"unity\": \"2022.3\",\n" +
"  \"author\": { \"name\": \"FunBrainLab, Keimyung University\" },\n  \"license\": \"Proprietary\"\n}\n");
            File.WriteAllText(Path.Combine(outPkg, "README.md"),
"# FunBrain VR Link (협력기관 배포판)\n\n" +
"이 패키지는 FunBrainLab(계명대학교 의용공학과)의 VR Link 를 협력기관 프로젝트용으로 묶은 것입니다.\n" +
"재배포·역공학·제3자 제공을 금지하며, 사용 범위는 협약에 따릅니다.\n\n" +
"## 사용법\n1. 이 폴더를 Unity 프로젝트의 `Packages/com.funbrainlab.vrlink/` 로 복사합니다.\n" +
(webrtc ? "2. 화면 보기에는 `com.unity.webrtc` 패키지가 필요합니다 (Package Manager 에서 설치).\n" : "2. 이 배포판에는 화면 보기(WebRTC)가 포함되어 있지 않습니다.\n") +
"3. 씬에 빈 오브젝트를 만들고 `VR Link` 컴포넌트(화면 보기를 쓰면 `Web Rtc Streamer` 도)를 추가합니다.\n" +
"4. 웹에서 조절할 변수에 `[Tunable]`, 보여 줄 값에 `[Monitor]`, 버튼으로 실행할 메서드에 `[TunableAction]` 을 붙입니다.\n" +
"5. 헤드셋에서 처음 실행하면 연결 코드를 묻습니다. 코드는 FunBrainLab 웹앱에서 발급받습니다.\n\n" +
"```csharp\nusing FunBrainLab.VRLink;\n\npublic class Task : MonoBehaviour\n{\n    [Tunable(0.1f, 2f, Label = \"속도\")] public float speed = 1f;\n" +
"    [Monitor(Widget = \"ring\", Label = \"진행률\")] public float progress;\n    [TunableAction(Label = \"시작\")] public void Begin() { }\n}\n```\n");
            File.WriteAllText(Path.Combine(outPkg, "LICENSE.txt"),
"Copyright (c) FunBrainLab, Keimyung University. All rights reserved.\n" +
"협약에 명시된 협력기관의 프로젝트에서만 사용할 수 있으며, 복제·재배포·역공학·제3자 제공을 금지합니다.\n");
        }

        [Serializable] class PkgJson { public string version = "0.0.0"; }
    }
}
