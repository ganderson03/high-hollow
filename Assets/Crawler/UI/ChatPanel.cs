using System;
using System.Collections.Generic;
using Crawler.Render;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Crawler.UI
{
    /// <summary>
    /// Scrolling chat log + input line, built in code with legacy uGUI Text (no TMP
    /// essentials import needed). Enter focuses the field; Enter again sends.
    /// </summary>
    public sealed class ChatPanel : MonoBehaviour
    {
        const int MaxLines = 80;

        public Action<string> OnSubmit;

        /// <summary>When true, Enter does not focus the chat field (e.g. a poster card is up).</summary>
        public Func<bool> Blocked;

        CanvasGroup _group;
        float _lastActivity;

        readonly Queue<string> _lines = new Queue<string>();
        Text _log;
        ScrollRect _scroll;
        InputField _field;
        int _submitFrame = -1;
        bool _scrollPending;

        public bool IsTyping => _field != null && _field.isFocused;

        void Awake() => Build();

        public void AddLine(string who, string text, Color whoColor)
        {
            var hex = ColorUtility.ToHtmlStringRGB(whoColor);
            _lines.Enqueue($"<color=#{hex}><b>{Escape(who)}</b></color>  {Escape(text)}");
            while (_lines.Count > MaxLines) _lines.Dequeue();
            _log.text = string.Join("\n", _lines);
            _scrollPending = true;
            _lastActivity = Time.unscaledTime;
        }

        public void AddSystem(string text) => AddLine("~", text, Palette.Ochre);

        void Update()
        {
            // Fade back when idle so the room stays readable; full strength while talking.
            if (IsTyping) _lastActivity = Time.unscaledTime;
            float target = Time.unscaledTime - _lastActivity < 8f ? 1f : 0.4f;
            _group.alpha = Mathf.MoveTowards(_group.alpha, target, Time.unscaledDeltaTime * 2f);

            var kb = Keyboard.current;
            if (kb != null && !_field.isFocused && Time.frameCount != _submitFrame
                && (Blocked == null || !Blocked())
                && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
            {
                _field.ActivateInputField();
                _field.Select();
            }
        }

        void LateUpdate()
        {
            if (!_scrollPending) return;
            _scrollPending = false;
            Canvas.ForceUpdateCanvases();
            _scroll.verticalNormalizedPosition = 0f;
        }

        void Submit(string text)
        {
            var kb = Keyboard.current;
            bool enter = kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame);
            if (!enter) return; // focus lost by clicking away; keep draft
            _submitFrame = Time.frameCount;
            text = text.Trim();
            _field.text = "";
            _field.DeactivateInputField();
            if (text.Length > 0) OnSubmit?.Invoke(text);
        }

        static string Escape(string s) => s.Replace("<", "‹").Replace(">", "›");

        // ------------------------------------------------------------------ layout

        void Build()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var rt = transform as RectTransform;
            if (rt == null) rt = gameObject.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(0, 0);
            rt.pivot = new Vector2(0, 0);
            rt.anchoredPosition = new Vector2(16, 16);
            rt.sizeDelta = new Vector2(340, 190);

            _group = gameObject.AddComponent<CanvasGroup>();
            var bg = gameObject.AddComponent<Image>();
            bg.color = WithAlpha(Palette.Night, 0.82f);
            gameObject.AddComponent<Outline>().effectColor = WithAlpha(Palette.Sienna, 0.9f);

            // Header band: poster-style title strip.
            var header = Rect("Header", transform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -24), Vector2.zero);
            header.gameObject.AddComponent<Image>().color = Palette.Sienna;
            var title = Label("Title", header, font, 13, Palette.Cream, TextAnchor.MiddleLeft);
            title.text = "  CAMPFIRE  ·  chat";
            title.fontStyle = FontStyle.Bold;

            // Log.
            var view = Rect("Log", transform, new Vector2(0, 0), new Vector2(1, 1), new Vector2(8, 40), new Vector2(-8, -30));
            view.gameObject.AddComponent<RectMask2D>();
            _scroll = view.gameObject.AddComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 20f;

            var content = Rect("Content", view, new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, Vector2.zero);
            content.pivot = new Vector2(0, 0);
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _log = content.gameObject.AddComponent<Text>();
            _log.font = font; _log.fontSize = 14; _log.color = Palette.Cream;
            _log.supportRichText = true;
            _log.alignment = TextAnchor.LowerLeft;
            _log.horizontalOverflow = HorizontalWrapMode.Wrap;
            _log.verticalOverflow = VerticalWrapMode.Overflow;
            _scroll.content = content;
            _scroll.viewport = view;

            // Input line.
            var line = Rect("Input", transform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(8, 8), new Vector2(-8, 34));
            var lineBg = line.gameObject.AddComponent<Image>();
            lineBg.color = WithAlpha(Palette.Cream, 0.1f);
            _field = line.gameObject.AddComponent<InputField>();
            _field.targetGraphic = lineBg;
            var txt = Label("Text", line, font, 14, Palette.Cream, TextAnchor.MiddleLeft);
            txt.supportRichText = false;
            ((RectTransform)txt.transform).offsetMin = new Vector2(8, 0);
            var ph = Label("Placeholder", line, font, 14, WithAlpha(Palette.Sand, 0.45f), TextAnchor.MiddleLeft);
            ph.fontStyle = FontStyle.Italic;
            ph.text = "press Enter to talk";
            ((RectTransform)ph.transform).offsetMin = new Vector2(8, 0);
            _field.textComponent = txt;
            _field.placeholder = ph;
            _field.characterLimit = 200;
            _field.caretColor = Palette.Ember;
            _field.selectionColor = WithAlpha(Palette.Sienna, 0.6f);
            _field.onEndEdit.AddListener(Submit);
        }

        static RectTransform Rect(string n, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        {
            var go = new GameObject(n, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.offsetMin = oMin; rt.offsetMax = oMax;
            return rt;
        }

        static Text Label(string n, Transform parent, Font font, int size, Color c, TextAnchor anchor)
        {
            var rt = Rect(n, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font; t.fontSize = size; t.color = c; t.alignment = anchor;
            return t;
        }

        static Color WithAlpha(Color c, float a) { c.a = a; return c; }
    }
}
