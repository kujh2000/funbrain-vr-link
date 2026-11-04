using System;
using UnityEngine;
using UnityEngine.UI;

namespace FunBrainLab.VRLink
{
    /// <summary>
    /// 헤드셋 안에서 연결 코드를 입력하는 키패드. 코드로 만들어지므로 프리팹·폰트·TMP 가 필요 없습니다.
    ///
    /// 입력 방식 두 가지가 동시에 동작합니다.
    ///  - 포인터: 씬의 EventSystem 에 XR UI Input Module(XRI) 등 포인터가 있으면 일반 버튼처럼 눌립니다
    ///  - 시선(dwell): 머리 중앙 시선이 버튼 위에 1.1초 머물면 눌립니다 — 어떤 기종·입력 설정에서도 동작
    /// </summary>
    public sealed class PairingKeypad : MonoBehaviour
    {
        public float distance = 1.6f;
        public float dwellSeconds = 1.1f;
        public Action<string> OnSubmit;

        Canvas canvas;
        Text display, hint;
        Button okButton;
        string code = "";
        Button gazeTarget;
        float gazeTime;
        Image gazeFill;
        Camera cam;
        Font font;

        public static PairingKeypad Show(Transform parent, Action<string> onSubmit)
        {
            var go = new GameObject("VRLink Pairing Keypad");
            go.transform.SetParent(parent, false);
            var k = go.AddComponent<PairingKeypad>();
            k.OnSubmit = onSubmit;
            return k;
        }

        void Start()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            cam = Camera.main;
            Build();
            Place();
        }

        void Place()
        {
            var head = cam != null ? cam.transform : null;
            if (head == null) { transform.position = new Vector3(0, 1.4f, distance); return; }
            var fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            transform.position = head.position + fwd * distance;
            transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
        }

        void Build()
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            gameObject.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 2;
            gameObject.AddComponent<GraphicRaycaster>();
            var rt = (RectTransform)transform;
            rt.sizeDelta = new Vector2(640, 520);
            rt.localScale = Vector3.one * 0.0016f;

            var bg = MakeImage(rt, "BG", new Color(0.05f, 0.07f, 0.13f, 0.96f));
            Stretch(bg.rectTransform);

            MakeText(rt, "Title", "CONNECT CODE", 30, new Vector2(0, 220), new Vector2(600, 44), new Color(0.55f, 0.6f, 1f));
            hint = MakeText(rt, "Hint", "Enter the 6-letter code from the web app", 20, new Vector2(0, 182), new Vector2(600, 32), new Color(0.7f, 0.75f, 0.85f));
            display = MakeText(rt, "Display", "_ _ _ _ _ _", 48, new Vector2(0, 118), new Vector2(600, 70), Color.white);

            // 32자 키 (8열 x 4행) + 백스페이스/확인
            const int cols = 8;
            float w = 68, h = 56, gap = 6, x0 = -(cols * (w + gap) - gap) / 2 + w / 2, y0 = 40;
            for (int i = 0; i < PairingStore.Alphabet.Length; i++)
            {
                var ch = PairingStore.Alphabet[i];
                int r = i / cols, c = i % cols;
                var b = MakeButton(rt, ch.ToString(), ch.ToString(), new Vector2(x0 + c * (w + gap), y0 - r * (h + gap)), new Vector2(w, h));
                b.onClick.AddListener(() => Append(ch));
            }
            var del = MakeButton(rt, "Del", "<", new Vector2(-150, -220), new Vector2(200, 60));
            del.onClick.AddListener(Backspace);
            okButton = MakeButton(rt, "OK", "OK", new Vector2(150, -220), new Vector2(200, 60), new Color(0.3f, 0.75f, 0.55f));
            okButton.onClick.AddListener(Submit);
            okButton.interactable = false;
        }

        void Append(char c)
        {
            if (code.Length >= 6) return;
            code += c;
            Refresh();
        }

        void Backspace()
        {
            if (code.Length == 0) return;
            code = code.Substring(0, code.Length - 1);
            Refresh();
        }

        void Refresh()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 6; i++) { sb.Append(i < code.Length ? code[i] : '_'); if (i < 5) sb.Append(' '); }
            display.text = sb.ToString();
            okButton.interactable = code.Length == 6;
        }

        void Submit()
        {
            if (!PairingStore.IsValid(code)) { hint.text = "Code must be 6 letters"; return; }
            OnSubmit?.Invoke(code);
            Destroy(gameObject);
        }

        // ---------- 시선(dwell) 선택 ----------
        void Update()
        {
            if (cam == null) { cam = Camera.main; if (cam == null) return; }
            Button hit = null;
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            var plane = new Plane(-transform.forward, transform.position);
            if (plane.Raycast(ray, out var dist))
            {
                var world = ray.GetPoint(dist);
                foreach (var b in GetComponentsInChildren<Button>())
                {
                    if (!b.interactable) continue;
                    var r = (RectTransform)b.transform;
                    if (RectTransformUtility.RectangleContainsScreenPoint(r, world, null) || ContainsWorld(r, world)) { hit = b; break; }
                }
            }
            if (hit != gazeTarget)
            {
                SetGazeFill(0f);
                gazeTarget = hit;
                gazeTime = 0f;
                gazeFill = hit != null ? hit.transform.Find("GazeFill")?.GetComponent<Image>() : null;
            }
            else if (hit != null)
            {
                gazeTime += Time.unscaledDeltaTime;
                SetGazeFill(gazeTime / dwellSeconds);
                if (gazeTime >= dwellSeconds)
                {
                    gazeTime = -0.6f; // 연속 입력 방지
                    SetGazeFill(0f);
                    hit.onClick.Invoke();
                }
            }
        }

        static bool ContainsWorld(RectTransform r, Vector3 world)
        {
            var local = r.InverseTransformPoint(world);
            return r.rect.Contains(local);
        }

        void SetGazeFill(float t)
        {
            if (gazeFill != null) gazeFill.fillAmount = Mathf.Clamp01(t);
        }

        // ---------- UI 생성 도우미 ----------
        static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
        }

        Image MakeImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            return img;
        }

        Text MakeText(Transform parent, string name, string text, int size, Vector2 pos, Vector2 sizeDelta, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = pos; rt.sizeDelta = sizeDelta;
            var t = go.AddComponent<Text>();
            t.font = font; t.text = text; t.fontSize = size; t.color = color;
            t.alignment = TextAnchor.MiddleCenter; t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return t;
        }

        Button MakeButton(Transform parent, string name, string label, Vector2 pos, Vector2 size, Color? color = null)
        {
            var img = MakeImage(parent, "Key " + name, color ?? new Color(0.16f, 0.2f, 0.33f));
            var rt = img.rectTransform;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            var fill = MakeImage(rt, "GazeFill", new Color(0.55f, 0.6f, 1f, 0.45f));
            Stretch(fill.rectTransform);
            fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal; fill.fillAmount = 0f; fill.raycastTarget = false;
            var t = MakeText(rt, "Label", label, 28, Vector2.zero, size, Color.white);
            t.raycastTarget = false;
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var colors = b.colors; colors.disabledColor = new Color(1, 1, 1, 0.35f); b.colors = colors;
            return b;
        }
    }
}
