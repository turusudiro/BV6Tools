using SqlNado;
using System.Text.Json;

namespace BV6Tools.Services.Database.Models
{
    [SQLiteTable(Name = "Lua")]
    public class LuaDb
    {
        [SQLiteColumn(IsPrimaryKey = true)]
        public uint AppId { get; set; }
        [SQLiteColumn(Name = "Data")]
        public string DataJson
        {
            get => JsonSerializer.Serialize(new
            {
                AddAppId,
                ManifestID,
                AddToken,
                IsEnabled
            });
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    AddAppId = [];
                    ManifestID = [];
                    AddToken = [];
                    IsEnabled = false;
                    return;
                }

                using var doc = JsonDocument.Parse(value);
                var root = doc.RootElement;

                AddAppId = root.TryGetProperty(nameof(AddAppId), out var a)
                    ? JsonSerializer.Deserialize<List<AddAppIdData>>(a.GetRawText()) ?? []
                    : [];
                ManifestID = root.TryGetProperty(nameof(ManifestID), out var m)
                    ? JsonSerializer.Deserialize<List<SetManifestID>>(m.GetRawText()) ?? []
                    : [];
                AddToken = root.TryGetProperty(nameof(AddToken), out var t)
                    ? JsonSerializer.Deserialize<List<LuaToken>>(t.GetRawText()) ?? []
                    : [];
                IsEnabled = root.TryGetProperty(nameof(IsEnabled), out var e) && JsonSerializer.Deserialize<bool>(e.GetRawText());
            }
        }
        [SQLiteColumn(Ignore = true)]
        public List<AddAppIdData> AddAppId { get; set; } = [];
        [SQLiteColumn(Ignore = true)]
        public List<SetManifestID> ManifestID { get; set; } = [];
        [SQLiteColumn(Ignore = true)]
        public List<LuaToken> AddToken { get; set; } = [];
        [SQLiteColumn(Ignore = true)]
        public bool IsEnabled { get; set; }
    }
    public class LuaApp
    {
        public uint AppId { get; set; }
        public bool IsEnabled { get; set; }
    }

    public class LuaToken : LuaApp
    {
        public string? Token { get; set; }
    }

    public class SetManifestID : LuaApp
    {
        public string? GID { get; set; }
        public string? Size { get; set; }
    }
    public class AddAppIdData : LuaApp
    {
        public AddAppIdType AppType { get; set; }
        public string Key { get; set; } = string.Empty;

    }
    public enum AddAppIdType
    {
        Depot = 0,
        App = 1
    }
}
