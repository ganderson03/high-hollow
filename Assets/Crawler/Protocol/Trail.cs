using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Crawler.Protocol
{
    /// <summary>
    /// The order of the trail: biomes, each a run of levels, read from levels/trail.json. The
    /// server uses it to decide which level follows a win; the client uses it for poster text
    /// and the level select. Like level files, it is part of the contract.
    /// </summary>
    public sealed class TrailDef
    {
        public readonly List<TrailBiome> Biomes = new List<TrailBiome>();

        /// <summary>Where a level sits on the trail. False if the trail does not list it.</summary>
        public bool Locate(string levelId, out TrailBiome biome, out int index)
        {
            foreach (var b in Biomes)
            {
                int i = b.Levels.IndexOf(levelId);
                if (i >= 0) { biome = b; index = i; return true; }
            }
            biome = null;
            index = -1;
            return false;
        }

        /// <summary>The level after this one, crossing into the next biome at a finale; null at the end.</summary>
        public string Next(string levelId)
        {
            for (int bi = 0; bi < Biomes.Count; bi++)
            {
                int i = Biomes[bi].Levels.IndexOf(levelId);
                if (i < 0) continue;
                if (i + 1 < Biomes[bi].Levels.Count) return Biomes[bi].Levels[i + 1];
                for (int nb = bi + 1; nb < Biomes.Count; nb++)
                    if (Biomes[nb].Levels.Count > 0) return Biomes[nb].Levels[0];
                return null;
            }
            return null;
        }
    }

    public sealed class TrailBiome
    {
        public string Id;
        public string Name;
        public readonly List<string> Levels = new List<string>();

        public bool IsFinale(int index) => index == Levels.Count - 1;
    }

    public static class TrailCodec
    {
        public static TrailDef Parse(string json)
        {
            var o = JObject.Parse(json);
            var trail = new TrailDef();
            foreach (var token in (JArray)o["biomes"] ?? throw new FormatException("trail has no biomes"))
            {
                var bo = (JObject)token;
                var biome = new TrailBiome { Id = (string)bo["id"], Name = (string)bo["name"] ?? (string)bo["id"] };
                foreach (var l in (JArray)bo["levels"] ?? new JArray()) biome.Levels.Add((string)l);
                trail.Biomes.Add(biome);
            }
            return trail;
        }
    }
}
