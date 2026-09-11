using Crawler.Protocol;
using Crawler.Render;
using UnityEngine;
using UnityEngine.UI;

namespace Crawler.UI
{
    /// <summary>Top-left link status with a paw-print glyph that turns while connecting.</summary>
    public sealed class StatusBadge : MonoBehaviour
    {
        Text _label;
        RectTransform _paw;
        LinkStatus _status = LinkStatus.Idle;

        void Awake()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var rt = transform as RectTransform;
            if (rt == null) rt = gameObject.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(16, -16);
            rt.sizeDelta = new Vector2(360, 34);
            gameObject.AddComponent<Image>().color = new Color(Palette.Night.r, Palette.Night.g, Palette.Night.b, 0.82f);

            var paw = new GameObject("Paw", typeof(RectTransform));
            _paw = (RectTransform)paw.transform;
            _paw.SetParent(transform, false);
            _paw.anchorMin = _paw.anchorMax = new Vector2(0, 0.5f);
            _paw.anchoredPosition = new Vector2(18, 0);
            _paw.sizeDelta = new Vector2(24, 24);
            var tex = ProcArt.Paw;
            paw.AddComponent<Image>().sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));

            var lbl = new GameObject("Label", typeof(RectTransform));
            var lrt = (RectTransform)lbl.transform;
            lrt.SetParent(transform, false);
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(38, 0); lrt.offsetMax = new Vector2(-8, 0);
            _label = lbl.AddComponent<Text>();
            _label.font = font; _label.fontSize = 14; _label.color = Palette.Cream;
            _label.alignment = TextAnchor.MiddleLeft;
            _label.supportRichText = true;
        }

        public void Set(LinkStatus s, string detail)
        {
            _status = s;
            string tag;
            Color c;
            switch (s)
            {
                case LinkStatus.Mock:          tag = "MOCK FEED"; c = Palette.Ochre; break;
                case LinkStatus.Connecting:    tag = "CONNECTING"; c = Palette.Sand; break;
                case LinkStatus.Connected:     tag = "CONNECTED"; c = Palette.Cream; break;
                case LinkStatus.Disconnected:  tag = "DISCONNECTED"; c = Palette.Ember; break;
                case LinkStatus.Failed:        tag = "NO SERVER"; c = Palette.Ember; break;
                case LinkStatus.ProtocolError: tag = "PROTOCOL"; c = Palette.Ember; break;
                default:                       tag = "IDLE"; c = Palette.Sand; break;
            }
            _label.text = $"<b><color=#{ColorUtility.ToHtmlStringRGB(c)}>{tag}</color></b>  {detail}";
        }

        void Update()
        {
            bool spinning = _status == LinkStatus.Connecting || _status == LinkStatus.Idle;
            _paw.localRotation = spinning ? Quaternion.Euler(0, 0, -Time.unscaledTime * 180f) : Quaternion.identity;
        }
    }
}
