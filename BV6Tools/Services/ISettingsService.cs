using BV6Tools.Models;
using BV6Tools.Services.Database.Models;

namespace BV6Tools.Services;

public interface ISettingsService
{
    IEnumerable<ProfileDb> Profiles { get; }
    AppSettings Settings { get; }

    void DeleteProfile(int profileID);

    void Save(Action<AppSettings> update);
}