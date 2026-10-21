using System;
using System.IO;
using System.Text.RegularExpressions;
using Crawler.Protocol;

namespace Hollowline
{
    /// <summary>
    /// Hollowline: the authoritative server for High Hollow. It owns the simulation; clients
    /// render what it sends them and ask for things.
    ///
    ///   dotnet run -- [--port 7777] [--level foothills-1] [--levels PATH] [--quiet]
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            int port = IntArg(args, "--port", 7777);
            string levelId = StringArg(args, "--level", "foothills-1");
            string levelsDir = StringArg(args, "--levels", null) ?? FindLevels();
            bool quiet = Array.IndexOf(args, "--quiet") >= 0;

            if (levelsDir == null || !Directory.Exists(levelsDir))
            {
                Console.Error.WriteLine("no levels directory; pass --levels PATH");
                return 2;
            }

            var levels = new LevelLoader(levelsDir);
            var first = levels.Load(levelId);
            if (first == null)
            {
                Console.Error.WriteLine($"could not load level '{levelId}' from {levelsDir}");
                return 2;
            }

            Console.WriteLine($"hollowline: levels from {levelsDir}");
            var server = new Server(first, levels.Trail(), levels.Load, quiet);
            server.Run(port);
            return 0;
        }

        /// <summary>Walk up from the binary until the project's levels folder turns up.</summary>
        static string FindLevels()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "Assets", "StreamingAssets", "levels");
                if (Directory.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            return null;
        }

        static string StringArg(string[] args, string name, string fallback)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        static int IntArg(string[] args, string name, int fallback)
        {
            var s = StringArg(args, name, null);
            return s != null && int.TryParse(s, out int v) ? v : fallback;
        }
    }

    /// <summary>Reads level and trail files. Ids are whitelisted because a client may name one.</summary>
    sealed class LevelLoader
    {
        static readonly Regex SafeId = new Regex("^[a-z0-9_-]{1,32}$");
        readonly string _dir;

        public LevelLoader(string dir) { _dir = dir; }

        public LevelDef Load(string id)
        {
            if (id == null || !SafeId.IsMatch(id)) return null;
            var path = Path.Combine(_dir, id + ".json");
            if (!File.Exists(path)) return null;
            try { return LevelCodec.Parse(File.ReadAllText(path)); }
            catch (Exception e)
            {
                Console.Error.WriteLine($"level '{id}' failed to parse: {e.Message}");
                return null;
            }
        }

        public TrailDef Trail()
        {
            var path = Path.Combine(_dir, "trail.json");
            if (!File.Exists(path)) return null;
            try { return TrailCodec.Parse(File.ReadAllText(path)); }
            catch (Exception e)
            {
                Console.Error.WriteLine($"trail failed to parse: {e.Message}");
                return null;
            }
        }
    }
}
