using System;

namespace FunBrainLab.VRLink
{
    /// <summary>
    /// 웹앱에서 조절할 값 (웹 → 앱). 필드/속성에 붙입니다.
    /// 지원 타입: int, float, double, bool, string, enum
    ///
    /// Widget (비우면 타입 기본값):
    ///   int/float : slider(기본), stepper, number, knob
    ///   bool      : toggle(기본), checkbox
    ///   enum      : dropdown(기본), buttons, radio
    ///   string    : text(기본), textarea, color("#RRGGBB")
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    public sealed class TunableAttribute : Attribute
    {
        public float Min { get; }
        public float Max { get; }
        public bool HasRange => Max > Min;

        public string Label { get; set; }
        public string Group { get; set; }
        public string Widget { get; set; }
        public float Step { get; set; }
        public string Help { get; set; }
        /// <summary>단위 표시 (예: "cm", "초")</summary>
        public string Unit { get; set; }
        /// <summary>웹앱 표시 순서 (작을수록 위)</summary>
        public int Order { get; set; } = 1000;

        public TunableAttribute() { }
        public TunableAttribute(float min, float max) { Min = min; Max = max; }
    }

    /// <summary>
    /// 웹앱에 보여줄 상태 값 (앱 → 웹, 읽기 전용). 필드/속성에 붙입니다.
    /// 지원 타입: int, float, double, bool, string, enum, Vector2, Vector3
    ///
    /// Widget (비우면 타입 기본값):
    ///   int/float : number(기본), bar, ring, dial, sparkline, timer(초→mm:ss), trafficlight(Thresholds 필요)
    ///   bool      : led(기본), badge
    ///   enum      : badge(기본), trafficlight(Colors 로 색 지정)
    ///   string    : text(기본), badge
    ///   Vector2/3 : xy(기본, 2D 점 위치), text
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    public sealed class MonitorAttribute : Attribute
    {
        public string Label { get; set; }
        public string Group { get; set; }
        public string Widget { get; set; }
        public float Min { get; set; }
        public float Max { get; set; }
        public bool HasRange => Max > Min;
        public string Unit { get; set; }
        /// <summary>소수점 자리수 (기본 2)</summary>
        public int Decimals { get; set; } = 2;
        /// <summary>enum → 색 매핑. 예: "green,yellow,red" (enum 선언 순서대로). 색: green/yellow/red/gray/blue</summary>
        public string Colors { get; set; }
        /// <summary>숫자 → 신호등 경계. 예: "30,70" → 30 미만 green, 70 미만 yellow, 그 이상 red. Invert=true면 반대</summary>
        public string Thresholds { get; set; }
        public bool Invert { get; set; }
        /// <summary>전송 간격(초). 기본 0.5. 값이 바뀔 때만 전송됨</summary>
        public float Interval { get; set; } = 0.5f;
        public int Order { get; set; } = 1000;
        public string Help { get; set; }
    }

    /// <summary>
    /// 웹앱 버튼으로 실행할 동작 (웹 → 앱). 매개변수 없는 메서드에 붙입니다.
    /// Widget: button(기본), danger(빨간 버튼), primary(강조)
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class TunableActionAttribute : Attribute
    {
        public string Label { get; set; }
        public string Group { get; set; }
        public string Widget { get; set; }
        /// <summary>true면 웹앱에서 확인 창을 띄운 뒤 실행</summary>
        public bool Confirm { get; set; }
        public int Order { get; set; } = 1000;
        public string Help { get; set; }
    }
}
