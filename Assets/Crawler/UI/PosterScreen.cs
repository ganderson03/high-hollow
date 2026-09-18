using System;
using Crawler.Render;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Crawler.UI
{
    /// <summary>
    /// Full-screen 1970s park-poster card, used for the title screen and the win screen.
    /// Dismissable cards close on Enter, Space, or a click.
    /// </summary>
    public sealed class PosterScreen : MonoBehaviour
    {
        const float FadeRate = 3f;

        CanvasGroup _group;
        Text _title, _sub, _prompt;
        Texture2D _tex;
        float _target;
        bool _dismissable;
        Action _onDismiss;

        public bool IsShowing => _target > 0f || (_group != null && _group.alpha > 0.01f);

        /// <summary>Frame the card was dismissed on, so the same key press doesn't also act in-game.</summary>
        public int LastDismissFrame { get; private set; } = -1;

        void Awake()
        {
            var rt = transform as RectTransform;
            if (rt == null) rt = gameObject.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            gameObject.AddComponent<Image>().color = Palette.Night; // letterbox fill

            var art = new GameObject("Art", typeof(RectTransform));
            art.transform.SetParent(transform, false);
            _tex = ProcArt.Poster(960, 540);
            art.AddComponent<RawImage>().texture = _tex;
            var fit = art.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fit.aspectRatio = 16f / 9f;

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var badge = Label("Badge", font, 14, Palette.Night, FontStyle.Bold, new Vector2(0.5f, 0.945f), new Vector2(900, 24));
            badge.text = Typeset.Spaced("EST. 2026") + "   ·   " + Typeset.Spaced("A SNOW LEOPARD EXPEDITION");
            _title = Label("Title", font, 64, Palette.Night, FontStyle.Bold, new Vector2(0.5f, 0.855f), new Vector2(1250, 90));
            _sub = Label("Subtitle", font, 22, Palette.Sienna, FontStyle.BoldAndItalic, new Vector2(0.5f, 0.775f), new Vector2(1000, 36));
            _prompt = Label("Prompt", font, 20, Palette.Cream, FontStyle.Bold, new Vector2(0.5f, 0.055f), new Vector2(900, 34));

            // A short trail of paw prints across the foreground.
            var paw = Sprite.Create(ProcArt.Paw, new Rect(0, 0, ProcArt.Paw.width, ProcArt.Paw.height), new Vector2(0.5f, 0.5f));
            for (int i = 0; i < 6; i++)
            {
                var p = new GameObject("Paw" + i, typeof(RectTransform));
                var prt = (RectTransform)p.transform;
                prt.SetParent(transform, false);
                prt.anchorMin = prt.anchorMax = new Vector2(0.30f + i * 0.08f, i % 2 == 0 ? 0.135f : 0.155f);
                prt.sizeDelta = new Vector2(22, 22);
                prt.localRotation = Quaternion.Euler(0, 0, -80f);
                var img = p.AddComponent<Image>();
                img.sprite = paw;
                img.color = new Color(1f, 1f, 1f, 0.35f + i * 0.1f);
            }
        }

        public void Show(string title, string subtitle, string prompt, bool dismissable, Action onDismiss = null, bool instant = false)
        {
            _title.text = title;
            _sub.text = subtitle;
            _prompt.text = prompt;
            _dismissable = dismissable;
            _onDismiss = onDismiss;
            _target = 1f;
            _group.blocksRaycasts = true;
            if (instant) _group.alpha = 1f;
        }

        public void Hide()
        {
            _target = 0f;
            _group.blocksRaycasts = false;
        }

        void Update()
        {
            _group.alpha = Mathf.MoveTowards(_group.alpha, _target, FadeRate * Time.unscaledDeltaTime);
            if (_target > 0f)
            {
                var c = _prompt.color;
                c.a = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2.2f));
                _prompt.color = c;
            }

            if (!_dismissable || _target <= 0f || _group.alpha < 0.95f) return;
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            bool go = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
                      || (mouse != null && mouse.leftButton.wasPressedThisFrame);
            if (!go) return;

            LastDismissFrame = Time.frameCount;
            _dismissable = false;
            Hide();
            var cb = _onDismiss;
            _onDismiss = null;
            cb?.Invoke();
        }

        void OnDestroy()
        {
            if (_tex != null) Destroy(_tex);
        }

        Text Label(string n, Font font, int size, Color color, FontStyle style, Vector2 anchor, Vector2 box)
        {
            var go = new GameObject(n, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(transform, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.sizeDelta = box;
            var t = go.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.color = color;
            t.fontStyle = style;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return t;
        }
    }
}
