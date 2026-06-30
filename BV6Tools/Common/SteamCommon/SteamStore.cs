using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SteamCommon
{
    public static partial class SteamStore
    {
        private const string AppDetailsUri = "https://store.steampowered.com/api/appdetails?appids=";
        private const string FeaturedBaseUri = "https://store.steampowered.com/api/featured";
        private const string SearchBaseUri = "https://store.steampowered.com/search/suggest?term=";
        private static readonly HttpClient client = new();

        public static async Task<SteamAppDetailsResponse?> GetAppDetailsAsync(uint appid, CancellationToken cancellationToken = default)
        {
            var uri = AppDetailsUri + appid;
            try
            {
                using var doc = await client.GetFromJsonAsync<JsonDocument>(uri, cancellationToken).ConfigureAwait(false);

                if (doc == null)
                {
                    return null;
                }

                if (!doc.RootElement.TryGetProperty(appid.ToString(), out var appNode))
                {
                    return null;
                }

                if (!appNode.TryGetProperty("success", out var successProp) || !successProp.GetBoolean())
                {
                    return null;
                }

                if (!appNode.TryGetProperty("data", out var dataProp))
                {
                    return null;
                }

                return dataProp.Deserialize<SteamAppDetailsResponse>();
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public static async Task<Dictionary<uint, SteamAppDetailsResponse?>> GetAppDetailsAsync(IEnumerable<uint> appids,
            int maxParallel = 5, CancellationToken cancellationToken = default)
        {
            ConcurrentDictionary<uint, SteamAppDetailsResponse?> result = new();
            using SemaphoreSlim semaphore = new(maxParallel);

            var tasks = appids.Distinct().Select(async appid =>
            {
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var detail = await GetAppDetailsAsync(appid, cancellationToken).ConfigureAwait(false);
                    if (detail != null)
                        result[appid] = detail;
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks).ConfigureAwait(false);
            return new Dictionary<uint, SteamAppDetailsResponse?>(result);
        }

        public static async IAsyncEnumerable<SteamAppDetailsResponse> GetAppDetailsStreamAsync(
            IEnumerable<uint> appids,
            int maxParallel = 5,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using var semaphore = new SemaphoreSlim(maxParallel);

            var tasks = appids
                .Distinct()
                .Select(async appid =>
                {
                    await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        return await GetAppDetailsAsync(appid, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                })
                .ToList();

            while (tasks.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var finished = await Task.WhenAny(tasks).ConfigureAwait(false);
                tasks.Remove(finished);

                var detail = await finished.ConfigureAwait(false);
                if (detail != null)
                {
                    yield return detail.Value;
                }
            }
        }

        public static async Task<SteamFeaturedResponse?> GetFeaturedAsync(CancellationToken token = default)
        {
            using var doc = await client.GetFromJsonAsync<JsonDocument>(FeaturedBaseUri, token).ConfigureAwait(false);

            if (doc == null)
            {
                return null;
            }

            if (!doc.RootElement.TryGetProperty("status", out var appNode))
            {
                return null;
            }

            if (appNode.GetInt32() != 1)
            {
                return null;
            }

            return doc.Deserialize<SteamFeaturedResponse>();
        }

        #region Steam Store Search

        [GeneratedRegex(@"data-ds-appid=""(\d+)""", RegexOptions.Compiled)]
        private static partial Regex AppIDStoreSearchRegex { get; }

        [GeneratedRegex(@"class=""match_name""[^>]*>(.*?)<", RegexOptions.Compiled)]
        private static partial Regex NameStoreSearchRegex { get; }

        public static async Task<IReadOnlyCollection<SteamStoreSearchResult>?> Search(string name, CancellationToken token = default)
        {
            string uri = SearchBaseUri + name + "&f=games&cc=us";
            HttpResponseMessage response = await client.GetAsync(uri, token);
            if (!response.IsSuccessStatusCode) return null;

            string html = await response.Content.ReadAsStringAsync(token);

            var appIds = AppIDStoreSearchRegex.Matches(html);
            var names = NameStoreSearchRegex.Matches(html);
            if (appIds.Count != names.Count) return [];

            return [.. appIds
                .Zip(names, (idMatch, nameMatch) =>
                {
                    if (!uint.TryParse(idMatch.Groups[1].Value, out var appId))
                        return (SteamStoreSearchResult?)null;

                    string appName = WebUtility.HtmlDecode(nameMatch.Groups[1].Value);
                    return new SteamStoreSearchResult(appId, appName);
                })
                .OfType<SteamStoreSearchResult>()];
        }

        #endregion Steam Store Search
    }

    #region Model

    public readonly record struct SteamStoreSearchResult(uint AppId, string Name);

    public readonly record struct SteamFeaturedResponse(
        [property: JsonPropertyName("featured_win")] IReadOnlyCollection<SteamFeaturedResponse.Featured> FeaturedWin
    )
    {
        public readonly record struct Featured(
            [property: JsonPropertyName("id")] uint AppId,
            [property: JsonPropertyName("name")] string Name,
            [property: JsonPropertyName("large_capsule_image")] string LargeImage,
            [property: JsonPropertyName("small_capsule_image")] string SmallImage,
            [property: JsonPropertyName("header_image")] string HeaderImage
            );
    };

    public readonly record struct SteamAppDetailsResponse(
        [property: JsonPropertyName("steam_appid")] uint AppId,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("dlc")] IReadOnlyCollection<uint> DLC,
        [property: JsonPropertyName("header_image")] string HeaderImage,
        [property: JsonPropertyName("type"), JsonConverter(typeof(SteamAppDetailsTypeConverter))] SteamAppDetailsType Type,
        [property: JsonPropertyName("fullgame")] FullGame FullGame
        );
    public readonly record struct FullGame(
        [property: JsonPropertyName("appid"), JsonConverter(typeof(StringToUIntConverter))] uint AppId,
        [property: JsonPropertyName("name")] string Name
        );

    #endregion Model

    #region JsonConverter

    [JsonConverter(typeof(SteamAppDetailsTypeConverter))]
    public enum SteamAppDetailsType
    {
        Game,
        DLC,
        Other
    }

    internal sealed class SteamAppDetailsTypeConverter : JsonConverter<SteamAppDetailsType>
    {
        public override SteamAppDetailsType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
                return SteamAppDetailsType.Other;

            return reader.GetString() switch
            {
                "game" => SteamAppDetailsType.Game,
                "dlc" => SteamAppDetailsType.DLC,
                _ => SteamAppDetailsType.Other
            };
        }

        public override void Write(Utf8JsonWriter writer, SteamAppDetailsType value, JsonSerializerOptions options)
            => writer.WriteStringValue(value switch
            {
                SteamAppDetailsType.Game => "game",
                SteamAppDetailsType.DLC => "dlc",
                _ => "other"
            });
    }

    internal sealed class StringToUIntConverter : JsonConverter<uint>
    {
        public override uint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String &&
                uint.TryParse(reader.GetString(), out var v))
                return v;

            if (reader.TokenType == JsonTokenType.Number)
                return reader.GetUInt32();

            return 0;
        }

        public override void Write(Utf8JsonWriter writer, uint value, JsonSerializerOptions options)
            => writer.WriteNumberValue(value);
    }

    #endregion JsonConverter
}