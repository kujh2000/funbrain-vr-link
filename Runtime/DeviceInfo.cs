using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace FunBrainLab.VRLink
{
    /// <summary>헤드셋 종류와 렌더링 환경을 실행 시 자동 감지합니다.</summary>
    public sealed class DeviceInfo
    {
        public string Manufacturer = "";
        public string Model = "";
        /// <summary>ViveFocusVision / ViveOther / Quest3 / Quest3S / MetaOther / Editor / Unknown</summary>
        public string Kind = "Unknown";
        public string GraphicsApi = "";
        /// <summary>MultiPass / SinglePass / SinglePassInstanced / SinglePassMultiview / None</summary>
        public string StereoMode = "None";
        public int EyeWidth;
        public int EyeHeight;
        public float RefreshRate;
        public string Os = "";
        public string XrDevice = "";

        public bool IsVive => Kind.StartsWith("Vive");
        public bool IsMeta => Kind.StartsWith("Quest") || Kind == "MetaOther";

        public static DeviceInfo Detect()
        {
            var d = new DeviceInfo
            {
                Manufacturer = AndroidBuild("MANUFACTURER") ?? "",
                Model = SystemInfo.deviceModel ?? "",
                GraphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                Os = SystemInfo.operatingSystem ?? "",
                XrDevice = XRSettings.loadedDeviceName ?? ""
            };
            var product = AndroidBuild("PRODUCT");
            if (!string.IsNullOrEmpty(product)) d.Model += $" ({product})";

            if (XRSettings.enabled)
            {
                d.StereoMode = XRSettings.stereoRenderingMode.ToString();
                d.EyeWidth = XRSettings.eyeTextureWidth;
                d.EyeHeight = XRSettings.eyeTextureHeight;
            }

            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            foreach (var disp in displays)
            {
                if (disp.running && disp.TryGetDisplayRefreshRate(out var rate)) { d.RefreshRate = rate; break; }
            }
            if (d.RefreshRate <= 0) d.RefreshRate = (float)Screen.currentResolution.refreshRateRatio.value;

            d.Kind = Classify(d);
            return d;
        }

        static string Classify(DeviceInfo d)
        {
            if (Application.isEditor) return "Editor";
            var s = $"{d.Manufacturer} {d.Model} {d.XrDevice}".ToLowerInvariant();
            if (s.Contains("quest 3s") || s.Contains("quest3s")) return "Quest3S";
            if (s.Contains("quest 3") || s.Contains("quest3") || s.Contains("eureka")) return "Quest3";
            if (s.Contains("quest") || s.Contains("oculus") || s.Contains("meta")) return "MetaOther";
            bool vive = s.Contains("htc") || s.Contains("vive");
            if (vive && s.Contains("vision")) return "ViveFocusVision";
            if (vive) return "ViveOther";
            return "Unknown";
        }

        static string AndroidBuild(string field)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var build = new AndroidJavaClass("android.os.Build");
                return build.GetStatic<string>(field);
            }
            catch { return null; }
#else
            return null;
#endif
        }

        public Dictionary<string, object> ToDict() => new Dictionary<string, object>
        {
            ["kind"] = Kind,
            ["manufacturer"] = Manufacturer,
            ["model"] = Model,
            ["xrDevice"] = XrDevice,
            ["graphicsApi"] = GraphicsApi,
            ["stereoMode"] = StereoMode,
            ["eyeWidth"] = EyeWidth,
            ["eyeHeight"] = EyeHeight,
            ["refreshRate"] = RefreshRate,
            ["os"] = Os
        };

        public override string ToString() =>
            $"{Kind} | {GraphicsApi} | {StereoMode} | {EyeWidth}x{EyeHeight} @{RefreshRate:F0}Hz";
    }
}
