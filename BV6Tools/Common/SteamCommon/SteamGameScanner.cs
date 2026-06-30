using BV6Tools.Extensions;
using SteamKit2;
using System.IO;
using System.Text.RegularExpressions;

namespace SteamCommon
{
    public partial class SteamGameScanner
    {
        public static IReadOnlyDictionary<uint, string> GetInstalledGames(string steamPath)
        {
            var games = new Dictionary<uint, string>();

            foreach (var library in GetLibraryFolders(steamPath))
            {
                var steamappsDir = Path.Combine(library, "steamapps");
                if (!Directory.Exists(steamappsDir))
                {
                    continue;
                }
                foreach (var acf in Directory.GetFiles(steamappsDir, @"appmanifest*"))
                {
                    var kv = new KeyValue();
                    using (var fs = new FileStream(acf, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        if (!kv.ReadAsText(fs)) continue;
                    }

                    var match = AppIDAcfRegex().Match(acf);

                    uint fallbackId = match.Success ? uint.Parse(match.Value) : 0;

                    var appid = kv["appid"].AsUnsignedInteger(fallbackId);

                    if (appid == 0) continue;

                    games.TryAdd(appid, kv["name"].Value ?? string.Empty);
                }
            }

            return games;
        }

        /// <exception cref="InvalidOperationException"></exception>
        public static HashSet<string> GetLibraryFolders(string steamPath)
        {
            if (steamPath.IsNullOrWhiteSpace())
            {
                throw new InvalidOperationException("Steam installation not found.");
            }

            var libFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steamPath };
            var configPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(configPath))
            {
                return libFolders;
            }

            try
            {
                using var fs = new FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var kv = new KeyValue();
                kv.ReadAsText(fs);
                foreach (var dir in GetLibraryFolders(kv))
                {
                    if (Directory.Exists(dir))
                    {
                        libFolders.Add(dir);
                    }
                }
            }
            catch (Exception)
            {
                throw;
            }

            return libFolders;
        }

        public static List<string> GetLibraryFolders(KeyValue foldersData)
        {
            var dbs = new List<string>();
            foreach (var child in foldersData.Children)
            {
                if (int.TryParse(child.Name, out int _))
                {
                    if (!child.Value.IsNullOrEmpty())
                    {
                        dbs.Add(child.Value);
                    }
                    else if (child.Children.Count >= 1)
                    {
                        var path = child.Children.FirstOrDefault(a => a.Name?.Equals("path", StringComparison.OrdinalIgnoreCase) == true);
                        if (path?.Value.IsNullOrEmpty() == false)
                        {
                            dbs.Add(path.Value);
                        }
                    }
                }
            }

            return dbs;
        }

        [GeneratedRegex(@"(?<=_)\d+(?=\.acf)")]
        private static partial Regex AppIDAcfRegex();
    }
}