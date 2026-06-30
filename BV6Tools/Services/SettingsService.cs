using BV6Tools.Models;
using BV6Tools.Services.Database;
using BV6Tools.Services.Database.Models;
using FileSystemCommon;
using System.IO;
using System.Text.Json;

namespace BV6Tools.Services;

public class SettingsService : ISettingsService
{
    private static readonly string SettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_settings.json");

    private readonly DatabaseService _databaseService;
    private readonly JsonSerializerOptions _jsonSerializerOptions;
    private readonly AppSettings _settings;

    public SettingsService(DatabaseService databaseService)
    {
        bool firstRun = false;

        _jsonSerializerOptions = new JsonSerializerOptions()
        {
            WriteIndented = true
        };

        _databaseService = databaseService;

        try
        {
            var json = File.ReadAllText(SettingsPath);
            _settings = JsonSerializer.Deserialize<AppSettings>(json)!;
        }
        catch
        {
            _settings = new AppSettings();
            try
            {
                _settings.SteamPath = FileSystem.GetExactPathName(SteamCommon.Steam.GetSteamDirectory());
            }
            catch { }
            finally { firstRun = true; }
        }

        Settings = _settings.Clone();

        if (firstRun)
        {
            if (!Profiles.Any())
            {
                var defaultProfile = _databaseService.CreateProfile("Default");
                Save(s =>
                {
                    s.ActiveProfileId = defaultProfile.ProfileID;
                });
            }
            else
            {
                var json = JsonSerializer.Serialize(Settings, _jsonSerializerOptions);

                File.WriteAllText(SettingsPath, json);
            }
        }
    }

    public IEnumerable<ProfileDb> Profiles => _databaseService.GetAllProfiles();

    public AppSettings Settings { get; }

    public void DeleteProfile(int profileID)
    {
        _databaseService.DeleteProfile(profileID);
    }

    public void Save(Action<AppSettings> update)
    {
        update(_settings);
        update(Settings);
        var json = JsonSerializer.Serialize(_settings, _jsonSerializerOptions);
        File.WriteAllText(SettingsPath, json);
    }
}