using System;
using System.IO;
using System.Text.RegularExpressions;
using Crawler.Protocol;
using UnityEngine;

namespace Crawler.App
{
    /// <summary>Loads StreamingAssets/levels/{id}.json. The id may come from a server, so it is whitelisted.</summary>
    static class LevelStore
    {
        static readonly Regex SafeId = new Regex("^[a-z0-9_-]{1,32}$");

        public static LevelDef Load(string id, out string error)
        {
            error = null;
            if (id == null || !SafeId.IsMatch(id))
            {
                error = $"refusing level id '{id}'";
                return null;
            }
            var path = Path.Combine(Application.streamingAssetsPath, "levels", id + ".json");
            try
            {
                return LevelCodec.Parse(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                error = $"level '{id}': {e.Message}";
                return null;
            }
        }
    }
}
