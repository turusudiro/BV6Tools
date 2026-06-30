using SteamKit2;
using System.IO;

namespace SteamCommon
{
    public static class SteamDepot
    {
        public static void SaveDecryptionKey(IReadOnlyDictionary<string, string> keyValuePairs, string vdfPath)
        {
            var kv = new KeyValue();
            using (var fs = new FileStream(vdfPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (!kv.ReadAsText(fs)) throw new InvalidOperationException($"Cannot parse {vdfPath}");
            }

            var steam = kv["Software"]["Valve"]["Steam"] ?? throw new InvalidOperationException("Cannot parse config.vdf");

            if (steam["depots"] == KeyValue.Invalid) steam.Children.Add(new KeyValue("depots"));

            var depots = steam["depots"];

            foreach (var (appid, key) in keyValuePairs)
            {
                bool isDuplicate = depots.Children.Any(d => d.Name == appid && d["DecryptionKey"]?.Value == key);

                if (!isDuplicate)
                {
                    var newDepot = new KeyValue(appid);
                    newDepot.Children.Add(new KeyValue("DecryptionKey", key));
                    depots.Children.Add(newDepot);
                }
            }

            using var stream = File.Open(vdfPath, FileMode.Create, FileAccess.Write);
            kv.SaveToStream(stream, false);
        }
    }
}
