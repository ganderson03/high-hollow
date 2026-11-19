using Crawler.Render;
using UnityEngine;
using UnityEngine.UI;

namespace Crawler.UI
{
    /// <summary>
    /// Top-right key: what each piece is and what it does, plus the controls. Static text.
    ///
    /// The rules described here are the core rules, which live in the client's mock (Crawler.Mock)
    /// and the Rust server (server/src/mechanics/core.rs). If they change, this text has to change
    /// with them. Swatch colors follow
    /// the dominant color of each look and hold for both art styles.
    /// </summary>
    public sealed class Legend : MonoBehaviour
    {
        const float Width = 300f;

        /// <summary>Builds the key's text and swatches.</summary>
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
                $"{S(Palette.Ochre, "◎")}  the leopard you are steering\n" +
                $"{S(Palette.Sienna, "●")}{S(Palette.Pine, "●")} scarves: sienna and pine leopards\n" +
                $"{S(Palette.Pine, "■")}  lever: Space beside it, opens a door\n" +
                $"{S(Palette.Stone, "■")}  plate: holds a door open while stood on\n" +
                $"{S(Palette.Umber, "■")}  door: opened by a lever or plate\n" +
                $"{S(Palette.Umber, "■")}  summit gate: carry every key\n" +
                $"{S(Palette.Ochre, "■")}  key: walk over it to carry it\n" +
                $"{S(Palette.Boulder, "◉")}  pit: back to the room's entrance, your keys lost\n" +
                $"{S(Palette.Stone, "■")}  sightstone: stand on it, Space: see this room true while you stay\n" +
                $"{S(Palette.Sand, "?")}  some pits are hidden; some keys are false\n" +
                $"{S(Palette.Cream, "◆")}  summit: both leopards, to win\n" +
                $"\n{C(Palette.Sand, "WASD move · Space act · Tab switch leopard · Enter chat")}";
        }

        /// <summary>Wraps text in a rich-text color tag.</summary>
        static string C(Color c, string s) => $"<color=#{ColorUtility.ToHtmlStringRGB(c)}>{s}</color>";

        /// <summary>A swatch: the glyph set larger than body text so the color actually reads.</summary>
        static string S(Color c, string glyph) => $"<size=17>{C(c, glyph)}</size>";
    }
}
