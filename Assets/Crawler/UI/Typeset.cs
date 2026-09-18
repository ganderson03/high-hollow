using System.Text;

namespace Crawler.UI
{
    /// <summary>Poster lettering helpers (the built-in font has no letter-spacing control).</summary>
    public static class Typeset
    {
        /// <summary>"HIGH HOLLOW" -> "H I G H   H O L L O W".</summary>
        public static string Spaced(string s)
        {
            var sb = new StringBuilder(s.Length * 2);
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0) sb.Append(s[i] == ' ' || s[i - 1] == ' ' ? " " : " ");
                sb.Append(s[i] == ' ' ? " " : s[i].ToString());
            }
            return sb.ToString();
        }
    }
}
