using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace FunBrainLab.VRLink
{
    /// <summary>코드를 어떻게 정할지</summary>
    public enum PairingMode
    {
        /// <summary>처음 실행할 때 헤드셋 안에서 코드를 입력받는다 (웹앱에서 발급한 코드). 병원·외부 기관용</summary>
        AskOnFirstRun,
        /// <summary>기기 ID 로 코드를 만든다 (재설치해도 같은 코드). 연구실 시험용 — 헤드셋 화면의 코드를 웹앱에 등록</summary>
        AutoFromDevice,
        /// <summary>Inspector 의 fixedCode 사용. 기기 한 대 전용 빌드일 때만</summary>
        Fixed,
    }

    /// <summary>
    /// 페어링 코드 결정 순서:
    ///   1. Fixed 모드면 fixedCode
    ///   2. USB 로 주입된 파일 inbox/pairing.json {"code":"K7M2QX"} (읽은 뒤 저장하고 삭제)
    ///   3. 이전에 저장된 코드 (PlayerPrefs)
    ///   4. AutoFromDevice 면 기기 ID 해시 / AskOnFirstRun 이면 null → 키패드 표시
    /// </summary>
    public static class PairingStore
    {
        public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // I,O,0,1 제외
        const string PrefKey = "vrlink.pairingCode";

        public static bool IsValid(string code) =>
            !string.IsNullOrEmpty(code) && code.Length == 6 && Array.TrueForAll(code.ToCharArray(), c => Alphabet.IndexOf(c) >= 0);

        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder();
            foreach (var c in s.ToUpperInvariant()) if (Alphabet.IndexOf(c) >= 0) sb.Append(c);
            return sb.ToString();
        }

        /// <summary>저장된(또는 주입된·고정된) 코드. 없으면 null</summary>
        public static string Resolve(PairingMode mode, string fixedCode)
        {
            if (mode == PairingMode.Fixed)
            {
                var f = Normalize(fixedCode);
                if (IsValid(f)) return f;
                Debug.LogWarning("[VRLink] Fixed 모드인데 fixedCode 가 올바르지 않습니다 (6자, I·O·0·1 제외). 저장된 코드로 대체합니다.");
            }

            var injected = ReadInjected();
            if (injected != null) { Save(injected); return injected; }

            var saved = PlayerPrefs.GetString(PrefKey, "");
            if (IsValid(saved)) return saved;

            if (mode == PairingMode.AutoFromDevice)
            {
                var auto = FromDeviceId();
                Save(auto);
                return auto;
            }
            return null;
        }

        public static void Save(string code)
        {
            PlayerPrefs.SetString(PrefKey, code);
            PlayerPrefs.Save();
        }

        /// <summary>저장된 코드 지우기 — 다음 실행 때 다시 묻거나(Ask) 다시 만든다(Auto)</summary>
        public static void Clear()
        {
            PlayerPrefs.DeleteKey(PrefKey);
            PlayerPrefs.Save();
        }

        /// <summary>기기 ID + 앱 ID 해시 → 6자. 같은 기기·같은 앱이면 재설치해도 같은 코드</summary>
        public static string FromDeviceId()
        {
            var seed = SystemInfo.deviceUniqueIdentifier + "|" + Application.identifier;
            using var sha = SHA256.Create();
            var h = sha.ComputeHash(Encoding.UTF8.GetBytes(seed));
            var chars = new char[6];
            for (int i = 0; i < 6; i++) chars[i] = Alphabet[h[i] % Alphabet.Length];
            return new string(chars);
        }

        /// <summary>USB(ADB)로 밀어 넣은 inbox/pairing.json → 코드. 읽으면 파일 삭제</summary>
        static string ReadInjected()
        {
            try
            {
                var path = Path.Combine(Application.persistentDataPath, "vrlink", "inbox", "pairing.json");
                if (!File.Exists(path)) return null;
                var text = File.ReadAllText(path);
                File.Delete(path);
                var d = Json.ParseObject(text);
                var code = d != null && d.TryGetValue("code", out var c) ? Normalize(c?.ToString()) : "";
                if (IsValid(code)) { Debug.Log("[VRLink] USB 로 주입된 코드 적용"); return code; }
                Debug.LogWarning("[VRLink] pairing.json 의 code 가 올바르지 않습니다");
            }
            catch (Exception e) { Debug.LogWarning("[VRLink] pairing.json 읽기 실패: " + e.Message); }
            return null;
        }
    }
}
