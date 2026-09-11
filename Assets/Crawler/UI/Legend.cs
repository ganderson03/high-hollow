using Crawler.Render;
using UnityEngine;
using UnityEngine.UI;

namespace Crawler.UI
{
    /// <summary>Top-right key: what each piece is, plus the controls. Static text only.</summary>
    public sealed class Legend : MonoBehaviour
    {
        void Awake()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var rt = transform as RectTransform;
            if (rt == null) rt = gameObject.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1, 1);
            rt.anchoredPosition = new Vector2(-16, -16);
            rt.sizeDelta = new Vector2(250, 228);
            gameObject.AddComponent<Image>().color = new Color(Palette.Night.r, Palette.Night.g, Palette.Night.b, 0.82f);
            gameObject.AddComponent<Outline>().effectColor = Palette.Sienna;

            var t = new GameObject("Text", typeof(RectTransform));
            var trt = (RectTransform)t.transform;
            trt.SetParent(transform, false);
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(12, 8); trt.offsetMax = new Vector2(-10, -8);
            var text = t.AddComponent<Text>();
            text.font = font; text.fontSize = 13; text.color = Palette.Cream;
            text.supportRichText = true;
            text.alignment = TextAnchor.UpperLeft;
            text.text =
                $"<b>{C(Palette.Ochre, "FIELD GUIDE")}</b>\n" +
                $"{C(Palette.Ochre, "●")}  you (gold ring)\n" +
                $"{C(Palette.Cream, "●")}  teammates\n" +
                $"{C(Palette.Pine, "●")}  lever: stand beside, Space\n" +
                $"{C(Palette.Stone, "●")}  stone plate: hold to open\n" +
                $"{C(Palette.Umber, "●")}  door / summit gate\n" +
                $"{C(Palette.Ochre, "●")}  key: three open the gate\n" +
                $"{C(Palette.Sienna, "●")}  hazard pit: back to camp\n" +
                $"{C(Palette.Stone, "●")}  sightstone: Space to see true\n" +
                $"{C(Palette.Cream, "◉")}  summit\n" +
                $"\n{C(Palette.Sand, "WASD / arrows move · Enter chat")}";
        }

        static string C(Color c, string s) => $"<color=#{ColorUtility.ToHtmlStringRGB(c)}>{s}</color>";
    }
}
