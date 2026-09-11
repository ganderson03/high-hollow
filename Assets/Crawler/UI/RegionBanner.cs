using Crawler.Render;
using UnityEngine;
using UnityEngine.UI;

namespace Crawler.UI
{
    /// <summary>Poster-style room title that fades in at the top when you enter a new room.</summary>
    public sealed class RegionBanner : MonoBehaviour
    {
        const float FadeIn = 0.35f, Hold = 2.45f, FadeOut = 0.7f;

        CanvasGroup _group;
        Text _name, _sub;
        float _t = -1f;

        void Awake()
        {
            var rt = transform as RectTransform;
            if (rt == null) rt = gameObject.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0, -16);
            rt.sizeDelta = new Vector2(500, 74);

            _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            var band = gameObject.AddComponent<Image>();
            band.color = new Color(Palette.Sienna.r, Palette.Sienna.g, Palette.Sienna.b, 0.92f);
            var outline = gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(Palette.Cream.r, Palette.Cream.g, Palette.Cream.b, 0.6f);
            outline.effectDistance = new Vector2(2, -2);

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _name = Label("Name", font, 28, Palette.Cream, FontStyle.Bold, new Vector2(0, 0.40f), new Vector2(1, 1));
            _sub = Label("Subtitle", font, 14, Palette.Sand, FontStyle.Italic, new Vector2(0, 0.02f), new Vector2(1, 0.45f));
        }

        public void Show(string name, string subtitle)
        {
            _name.text = Typeset.Spaced((name ?? "").ToUpperInvariant());
            _sub.text = subtitle ?? "";
            _t = 0f;
        }

        void Update()
        {
            if (_t < 0f) return;
            _t += Time.unscaledDeltaTime;
            float a = _t < FadeIn ? _t / FadeIn
                    : _t < FadeIn + Hold ? 1f
                    : 1f - (_t - FadeIn - Hold) / FadeOut;
            if (_t >= FadeIn + Hold + FadeOut) { a = 0f; _t = -1f; }
            _group.alpha = Mathf.Clamp01(a);
        }

        Text Label(string n, Font font, int size, Color c, FontStyle style, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(n, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(transform, false);
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var t = go.AddComponent<Text>();
            t.font = font; t.fontSize = size; t.color = c; t.fontStyle = style;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return t;
        }
    }
}
