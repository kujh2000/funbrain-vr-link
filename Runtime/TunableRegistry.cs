using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace FunBrainLab.VRLink
{
    /// <summary>필드 또는 속성 하나에 대한 공통 접근자</summary>
    public abstract class MemberEntry
    {
        public string Id;
        public string Label;
        public string Group;
        public int Order;
        public MonoBehaviour Target;
        public FieldInfo Field;
        public PropertyInfo Property;

        public Type ValueType => Field != null ? Field.FieldType : Property.PropertyType;
        public bool Alive => Target != null;
        public object Get() => Field != null ? Field.GetValue(Target) : Property.GetValue(Target);

        public static string KindOf(Type t)
        {
            if (t == typeof(int) || t == typeof(long)) return "int";
            if (t == typeof(float) || t == typeof(double)) return "float";
            if (t == typeof(bool)) return "bool";
            if (t == typeof(string)) return "string";
            if (t.IsEnum) return "enum";
            if (t == typeof(Vector2)) return "vector2";
            if (t == typeof(Vector3)) return "vector3";
            return "unsupported";
        }
        public string Kind => KindOf(ValueType);

        /// <summary>웹으로 보낼 형태 (enum→이름, Vector→{x,y,z})</summary>
        public object GetForWire()
        {
            var v = Get();
            switch (v)
            {
                case Enum e: return e.ToString();
                case Vector2 v2: return new Dictionary<string, object> { ["x"] = v2.x, ["y"] = v2.y };
                case Vector3 v3: return new Dictionary<string, object> { ["x"] = v3.x, ["y"] = v3.y, ["z"] = v3.z };
                default: return v;
            }
        }
    }

    /// <summary>[Tunable] 변수 (웹 → 앱)</summary>
    public sealed class TunableEntry : MemberEntry
    {
        public TunableAttribute Attr;

        internal static bool IsSupported(Type t) =>
            t == typeof(int) || t == typeof(long) || t == typeof(float) || t == typeof(double) ||
            t == typeof(bool) || t == typeof(string) || t.IsEnum;

        public string Widget
        {
            get
            {
                if (!string.IsNullOrEmpty(Attr.Widget)) return Attr.Widget;
                switch (Kind)
                {
                    case "int": case "float": return "slider";
                    case "bool": return "toggle";
                    case "enum": return "dropdown";
                    default: return "text";
                }
            }
        }

        /// <summary>웹에서 받은 값을 변환·범위 제한 후 적용. 값이 바뀌면 true</summary>
        public bool Set(object raw)
        {
            if (!Alive) return false;
            if (!TryConvert(raw, ValueType, out var value)) return false;
            value = Clamp(value);
            var old = Get();
            if (Equals(old, value)) return false;
            if (Field != null) Field.SetValue(Target, value);
            else Property.SetValue(Target, value);
            return true;
        }

        object Clamp(object v)
        {
            if (Attr == null || !Attr.HasRange) return v;
            switch (v)
            {
                case int i: return Mathf.Clamp(i, Mathf.CeilToInt(Attr.Min), Mathf.FloorToInt(Attr.Max));
                case long l: return Math.Max((long)Math.Ceiling(Attr.Min), Math.Min((long)Math.Floor(Attr.Max), l));
                case float f: return Mathf.Clamp(f, Attr.Min, Attr.Max);
                case double d: return Math.Max(Attr.Min, Math.Min(Attr.Max, d));
                default: return v;
            }
        }

        static bool TryConvert(object raw, Type t, out object result)
        {
            result = null;
            try
            {
                var inv = CultureInfo.InvariantCulture;
                if (t == typeof(string)) { result = raw?.ToString() ?? ""; return true; }
                if (raw == null) return false;

                if (t == typeof(bool))
                {
                    if (raw is bool b) result = b;
                    else if (raw is string s) result = s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase);
                    else result = Convert.ToDouble(raw, inv) != 0;
                    return true;
                }
                if (t.IsEnum)
                {
                    if (raw is string s)
                    {
                        if (int.TryParse(s, NumberStyles.Integer, inv, out var idx)) { result = Enum.ToObject(t, idx); return true; }
                        result = Enum.Parse(t, s, true);
                        return true;
                    }
                    result = Enum.ToObject(t, Convert.ToInt32(raw, inv));
                    return true;
                }
                double num = raw is string str ? double.Parse(str, NumberStyles.Float, inv) : Convert.ToDouble(raw, inv);
                if (t == typeof(int)) result = (int)Math.Round(num);
                else if (t == typeof(long)) result = (long)Math.Round(num);
                else if (t == typeof(float)) result = (float)num;
                else if (t == typeof(double)) result = num;
                else return false;
                return true;
            }
            catch (Exception) { return false; }
        }
    }

    /// <summary>[Monitor] 변수 (앱 → 웹)</summary>
    public sealed class MonitorEntry : MemberEntry
    {
        public MonitorAttribute Attr;
        internal object LastSent;
        internal float NextSendTime;

        internal static bool IsSupported(Type t) =>
            TunableEntry.IsSupported(t) || t == typeof(Vector2) || t == typeof(Vector3);

        public string Widget
        {
            get
            {
                if (!string.IsNullOrEmpty(Attr.Widget)) return Attr.Widget;
                switch (Kind)
                {
                    case "int": case "float": return "number";
                    case "bool": return "led";
                    case "enum": return "badge";
                    case "vector2": case "vector3": return "xy";
                    default: return "text";
                }
            }
        }

        /// <summary>값이 바뀌었고 전송 간격이 지났으면 true (LastSent 갱신)</summary>
        internal bool TryTakeChanged(float now, out object wire)
        {
            wire = null;
            if (!Alive || now < NextSendTime) return false;
            var v = Get();
            if (ValuesEqual(v, LastSent)) return false;
            LastSent = v;
            NextSendTime = now + Mathf.Max(0.05f, Attr.Interval);
            wire = GetForWire();
            return true;
        }

        static bool ValuesEqual(object a, object b)
        {
            if (a == null || b == null) return a == b;
            switch (a)
            {
                case float f when b is float g: return Mathf.Approximately(f, g);
                case double d when b is double e: return Math.Abs(d - e) < 1e-9;
                case Vector2 v when b is Vector2 w: return v == w;
                case Vector3 v when b is Vector3 w: return v == w;
                default: return a.Equals(b);
            }
        }
    }

    /// <summary>[TunableAction] 메서드 (웹 → 앱)</summary>
    public sealed class ActionEntry
    {
        public string Id;
        public string Label;
        public string Group;
        public int Order;
        public TunableActionAttribute Attr;
        public MonoBehaviour Target;
        public MethodInfo Method;
        public bool Alive => Target != null;
        public string Widget => string.IsNullOrEmpty(Attr.Widget) ? "button" : Attr.Widget;

        public bool Invoke()
        {
            if (!Alive) return false;
            try { Method.Invoke(Target, null); return true; }
            catch (Exception e) { Debug.LogException(e); return false; }
        }
    }

    /// <summary>씬의 [Tunable]/[Monitor]/[TunableAction] 을 찾아 스키마를 만들고, 웹에서 온 값·명령을 적용합니다.</summary>
    public sealed class TunableRegistry
    {
        readonly Dictionary<string, TunableEntry> tunables = new Dictionary<string, TunableEntry>();
        readonly Dictionary<string, MonitorEntry> monitors = new Dictionary<string, MonitorEntry>();
        readonly Dictionary<string, ActionEntry> actions = new Dictionary<string, ActionEntry>();

        public IReadOnlyCollection<TunableEntry> Tunables => tunables.Values;
        public IReadOnlyCollection<MonitorEntry> Monitors => monitors.Values;
        public IReadOnlyCollection<ActionEntry> Actions => actions.Values;
        public int Count => tunables.Count + monitors.Count + actions.Count;

        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public void Scan()
        {
            tunables.Clear(); monitors.Clear(); actions.Clear();
            var behaviours = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var mb in behaviours)
            {
                if (mb == null) continue;
                var type = mb.GetType();
                foreach (var f in type.GetFields(Flags))
                {
                    var t = f.GetCustomAttribute<TunableAttribute>(true);
                    if (t != null)
                    {
                        if (TunableEntry.IsSupported(f.FieldType)) AddTunable(mb, f.Name, t, f, null);
                        else WarnType(mb, f.Name, f.FieldType, "Tunable");
                    }
                    var m = f.GetCustomAttribute<MonitorAttribute>(true);
                    if (m != null)
                    {
                        if (MonitorEntry.IsSupported(f.FieldType)) AddMonitor(mb, f.Name, m, f, null);
                        else WarnType(mb, f.Name, f.FieldType, "Monitor");
                    }
                }
                foreach (var p in type.GetProperties(Flags))
                {
                    var t = p.GetCustomAttribute<TunableAttribute>(true);
                    if (t != null)
                    {
                        if (!p.CanRead || !p.CanWrite) Debug.LogWarning($"[VRLink] {type.Name}.{p.Name}: [Tunable] 속성은 get/set 이 모두 있어야 합니다.");
                        else if (TunableEntry.IsSupported(p.PropertyType)) AddTunable(mb, p.Name, t, null, p);
                        else WarnType(mb, p.Name, p.PropertyType, "Tunable");
                    }
                    var m = p.GetCustomAttribute<MonitorAttribute>(true);
                    if (m != null)
                    {
                        if (!p.CanRead) Debug.LogWarning($"[VRLink] {type.Name}.{p.Name}: [Monitor] 속성은 get 이 있어야 합니다.");
                        else if (MonitorEntry.IsSupported(p.PropertyType)) AddMonitor(mb, p.Name, m, null, p);
                        else WarnType(mb, p.Name, p.PropertyType, "Monitor");
                    }
                }
                foreach (var me in type.GetMethods(Flags))
                {
                    var a = me.GetCustomAttribute<TunableActionAttribute>(true);
                    if (a == null) continue;
                    if (me.GetParameters().Length != 0) { Debug.LogWarning($"[VRLink] {type.Name}.{me.Name}: [TunableAction] 메서드는 매개변수가 없어야 합니다."); continue; }
                    AddAction(mb, me, a);
                }
            }
            Debug.Log($"[VRLink] 수집: Tunable {tunables.Count}, Monitor {monitors.Count}, Action {actions.Count}");
        }

        string MakeId<T>(Dictionary<string, T> dict, MonoBehaviour mb, string member)
        {
            var baseId = $"{Sanitize(mb.gameObject.name)}-{Sanitize(mb.GetType().Name)}-{Sanitize(member)}";
            var id = baseId; int n = 2;
            while (dict.ContainsKey(id)) id = $"{baseId}-{n++}";
            return id;
        }

        void AddTunable(MonoBehaviour mb, string member, TunableAttribute attr, FieldInfo f, PropertyInfo p)
        {
            var id = MakeId(tunables, mb, member);
            tunables[id] = new TunableEntry
            {
                Id = id, Attr = attr, Target = mb, Field = f, Property = p, Order = attr.Order,
                Label = string.IsNullOrEmpty(attr.Label) ? member : attr.Label,
                Group = string.IsNullOrEmpty(attr.Group) ? mb.gameObject.name : attr.Group
            };
        }

        void AddMonitor(MonoBehaviour mb, string member, MonitorAttribute attr, FieldInfo f, PropertyInfo p)
        {
            var id = MakeId(monitors, mb, member);
            monitors[id] = new MonitorEntry
            {
                Id = id, Attr = attr, Target = mb, Field = f, Property = p, Order = attr.Order,
                Label = string.IsNullOrEmpty(attr.Label) ? member : attr.Label,
                Group = string.IsNullOrEmpty(attr.Group) ? mb.gameObject.name : attr.Group
            };
        }

        void AddAction(MonoBehaviour mb, MethodInfo me, TunableActionAttribute attr)
        {
            var id = MakeId(actions, mb, me.Name);
            actions[id] = new ActionEntry
            {
                Id = id, Attr = attr, Target = mb, Method = me, Order = attr.Order,
                Label = string.IsNullOrEmpty(attr.Label) ? me.Name : attr.Label,
                Group = string.IsNullOrEmpty(attr.Group) ? mb.gameObject.name : attr.Group
            };
        }

        static void WarnType(MonoBehaviour mb, string name, Type t, string attr) =>
            Debug.LogWarning($"[VRLink] {mb.GetType().Name}.{name}: {t.Name} 타입은 [{attr}] 에서 지원하지 않습니다.");

        /// <summary>Firebase 키로 쓸 수 없는 문자( . $ # [ ] / 공백 )와 '-' 를 '_'로 바꿉니다.</summary>
        static string Sanitize(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
                sb.Append(c == '.' || c == '$' || c == '#' || c == '[' || c == ']' || c == '/' || char.IsWhiteSpace(c) || c == '-' ? '_' : c);
            return sb.ToString();
        }

        public bool TryGetTunable(string id, out TunableEntry e) => tunables.TryGetValue(id, out e);
        public bool TryGetAction(string id, out ActionEntry e) => actions.TryGetValue(id, out e);

        // ---------------- 스키마 ----------------

        public Dictionary<string, object> BuildSchema()
        {
            var controls = new List<object>();
            foreach (var e in tunables.Values)
            {
                if (!e.Alive) continue;
                var d = Common(e.Id, e.Label, e.Group, e.Order, e.Kind, e.Widget, e.Attr.Help);
                d["value"] = e.GetForWire();
                if (e.Attr.HasRange) { d["min"] = e.Attr.Min; d["max"] = e.Attr.Max; }
                if (e.Attr.Step > 0) d["step"] = e.Attr.Step;
                if (!string.IsNullOrEmpty(e.Attr.Unit)) d["unit"] = e.Attr.Unit;
                if (e.ValueType.IsEnum) d["options"] = new List<string>(Enum.GetNames(e.ValueType));
                controls.Add(d);
            }
            var mons = new List<object>();
            foreach (var e in monitors.Values)
            {
                if (!e.Alive) continue;
                var d = Common(e.Id, e.Label, e.Group, e.Order, e.Kind, e.Widget, e.Attr.Help);
                d["value"] = e.GetForWire();
                if (e.Attr.HasRange) { d["min"] = e.Attr.Min; d["max"] = e.Attr.Max; }
                if (!string.IsNullOrEmpty(e.Attr.Unit)) d["unit"] = e.Attr.Unit;
                d["decimals"] = e.Attr.Decimals;
                if (!string.IsNullOrEmpty(e.Attr.Colors)) d["colors"] = Split(e.Attr.Colors);
                if (!string.IsNullOrEmpty(e.Attr.Thresholds)) d["thresholds"] = SplitNumbers(e.Attr.Thresholds);
                if (e.Attr.Invert) d["invert"] = true;
                d["interval"] = e.Attr.Interval;
                if (e.ValueType.IsEnum) d["options"] = new List<string>(Enum.GetNames(e.ValueType));
                mons.Add(d);
            }
            var acts = new List<object>();
            foreach (var e in actions.Values)
            {
                if (!e.Alive) continue;
                var d = Common(e.Id, e.Label, e.Group, e.Order, "action", e.Widget, e.Attr.Help);
                if (e.Attr.Confirm) d["confirm"] = true;
                acts.Add(d);
            }
            return new Dictionary<string, object>
            {
                ["version"] = 2,
                ["controls"] = controls,
                ["monitors"] = mons,
                ["actions"] = acts
            };
        }

        static Dictionary<string, object> Common(string id, string label, string group, int order, string kind, string widget, string help)
        {
            var d = new Dictionary<string, object>
            {
                ["id"] = id, ["label"] = label, ["group"] = group, ["order"] = order, ["kind"] = kind, ["widget"] = widget
            };
            if (!string.IsNullOrEmpty(help)) d["help"] = help;
            return d;
        }

        static List<string> Split(string s)
        {
            var l = new List<string>();
            foreach (var p in s.Split(',')) { var t = p.Trim(); if (t.Length > 0) l.Add(t); }
            return l;
        }

        static List<object> SplitNumbers(string s)
        {
            var l = new List<object>();
            foreach (var p in s.Split(','))
                if (double.TryParse(p.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) l.Add(d);
            return l;
        }

        // ---------------- 값 ----------------

        public Dictionary<string, object> BuildValues()
        {
            var d = new Dictionary<string, object>();
            foreach (var e in tunables.Values) if (e.Alive) d[e.Id] = e.GetForWire();
            lastSentValues = d;
            return d;
        }

        Dictionary<string, object> lastSentValues;

        /// <summary>앱 코드가 [Tunable] 값을 직접 바꿨는지 확인 (마지막 전송값과 비교). 바뀐 것이 있으면 true</summary>
        public bool TunablesChangedSinceSent()
        {
            if (lastSentValues == null) return tunables.Count > 0;
            foreach (var e in tunables.Values)
            {
                if (!e.Alive) continue;
                if (!lastSentValues.TryGetValue(e.Id, out var prev)) return true;
                if (!Equals(prev, e.GetForWire())) return true;
            }
            return false;
        }

        /// <summary>모든 모니터 현재값 (연결 직후 전체 전송용)</summary>
        public Dictionary<string, object> BuildMonitorValues(float now)
        {
            var d = new Dictionary<string, object>();
            foreach (var e in monitors.Values)
            {
                if (!e.Alive) continue;
                e.LastSent = e.Get();
                e.NextSendTime = now + e.Attr.Interval;
                d[e.Id] = e.GetForWire();
            }
            return d;
        }

        /// <summary>바뀐 모니터 값만 모아서 반환 (없으면 null)</summary>
        public Dictionary<string, object> CollectChangedMonitors(float now)
        {
            Dictionary<string, object> d = null;
            foreach (var e in monitors.Values)
            {
                if (!e.TryTakeChanged(now, out var wire)) continue;
                d ??= new Dictionary<string, object>();
                d[e.Id] = wire;
            }
            return d;
        }

        /// <summary>{id: 값} 묶음을 적용하고, 실제로 바뀐 id 목록을 돌려줍니다.</summary>
        public List<string> Apply(Dictionary<string, object> values)
        {
            var changed = new List<string>();
            if (values == null) return changed;
            foreach (var kv in values)
                if (tunables.TryGetValue(kv.Key, out var e) && e.Set(kv.Value)) changed.Add(kv.Key);
            return changed;
        }
    }
}
