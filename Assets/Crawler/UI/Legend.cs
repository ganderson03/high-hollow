using Crawler.Render;
using UnityEngine;
using UnityEngine.UI;

namespace Crawler.UI
{
    /// <summary>
    /// Top-right key: a swatch and a name for each piece, plus the controls. Static text.
    ///
    /// Names only. A lever explains itself, and a wall of sentences in the corner gets read
    /// once and then ignored. The two lines at the foot stay because they cannot be guessed
    /// by looking: that both leopards have to arrive, and that a pit costs the keys you are
    /// carrying. Those are the demo rules and today they live only in the mock, so if the
    /// server's rules change this text has to change with them. Swatch colors follow the
    /// dominant color of each look and hold for both art styles.
    /// </summary>
    public sealed class Legend : MonoBehaviour
    {
        const float Width = 212f;

        void Awake()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var rt = transform as RectTransform;
            if (rt == null) rt = gameObject.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1, 1);
            rt.anchoredPosition = new Vector2(-16, -16);
            rt.sizeDelta = new Vector2(Width, 0f);
            gameObject.AddComponent<Image>().color = new Color(Palette.Night.r, Palette.Night.g, Palette.Night.b, 0.82f);
            gameObject.AddComponent<Outline>().effectColor = Palette.Sienna;

            // The panel grows to fit its text, so wording can change without re-measuring.
            var layout = gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 10, 8, 10);
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var t = new GameObject("Text", typeof(RectTransform));
            t.transform.SetParent(transform, false);
            var text = t.AddComponent<Text>();
            text.font = font; text.fontSize = 13; text.color = Palette.Cream;
            text.lineSpacing = 1.05f;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.alignment = TextAnchor.UpperLeft;
            text.text =
                $"<b>{C(Palette.Ochre, "FIELD GUIDE")}</b>\n" +
                $"{S(Palette.Ochre, "◎")}  yours\n" +
                $"{S(Palette.Sienna, "●")}{S(Palette.Pine, "●")} sienna · pine\n" +
                $"{S(Palette.Pine, "■")}  lever\n" +
                $"{S(Palette.Stone, "■")}  plate\n" +
                $"{S(Palette.Umber, "■")}  door\n" +
                $"{S(Palette.Umber, "■")}  summit gate\n" +
                $"{S(Palette.Ochre, "■")}  key\n" +
                $"{S(Palette.Boulder, "◉")}  pit\n" +
                $"{S(Palette.Stone, "■")}  sightstone\n" +
                $"{S(Palette.Cream, "◆")}  summit\n" +
                $"\n{C(Palette.Sand, "both leopards reach the summit\na pit costs the keys you carry")}\n" +
                $"\n{C(Palette.Sand, "WASD move · Space act\nTab switch · Enter chat")}";
        }

        static string C(Color c, string s) => $"<color=#{ColorUtility.ToHtmlStringRGB(c)}>{s}</color>";

        /// <summary>A swatch: the glyph set larger than body text so the color actually reads.</summary>
        static string S(Color c, string glyph) => $"<size=17>{C(c, glyph)}</size>";
    }
}
