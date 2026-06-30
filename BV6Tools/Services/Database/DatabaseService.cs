using AppPathsCommon;
using BV6Tools.Services.Database.Models;
using SqlNado;

namespace BV6Tools.Services.Database
{


    public class DatabaseService
    {
        private readonly SQLiteSaveOptions _insertOptions;

        public DatabaseService()
        {
            Database = new SQLiteDatabase(AppPaths.DbPath);
            AppidsTableName = Database.SynchronizeSchema<AppDb>().Name;
            GamesTableName = Database.SynchronizeSchema<GameDb>().Name;
            ItemsTableName = Database.SynchronizeSchema<ItemDb>().Name;
            ProfileTableName = Database.SynchronizeSchema<ProfileDb>().Name;
            FeaturedCacheTableName = Database.SynchronizeSchema<FeaturedCacheDb>().Name;

            Database.SynchronizeSchema<ApplistCacheDb>();
            Database.SynchronizeSchema<TicketDb>();

            CreateIfNotLoadedOptions = new(Database)
            {
                CreateIfNotLoaded = true,
                TestTableExists = true,
            };
            _insertOptions = new SQLiteSaveOptions(Database) { DontTryUpdate = true };

            ApplistCache = Database.LoadAll<ApplistCacheDb>(CreateIfNotLoadedOptions).ToDictionary(x => x.AppID, x => x.Name) ?? [];
        }

        public string AppidsTableName { get; }
        public Dictionary<uint, string> ApplistCache { get; }
        public SQLiteLoadOptions CreateIfNotLoadedOptions { get; }
        public SQLiteDatabase Database { get; }
        private string FeaturedCacheTableName { get; }
        private string GamesTableName { get; }
        private string ItemsTableName { get; }
        private string ProfileTableName { get; }

        public ProfileDb CreateProfile(string name)
        {
            var profile = new ProfileDb { ProfileName = name };
            Database.Save(profile, _insertOptions);
            return Database.LoadByPrimaryKey<ProfileDb>((uint)Database.LastInsertRowId)!;
        }

        public void DeleteProfile(int profileID)
        {
            Database.ExecuteNonQuery($"DELETE FROM {ItemsTableName} WHERE ProfileID = {profileID}");
            Database.ExecuteNonQuery($"DELETE FROM {GamesTableName} WHERE ProfileID = {profileID}");
            Database.ExecuteNonQuery($"DELETE FROM {AppidsTableName} WHERE ProfileID = {profileID}");
            Database.ExecuteNonQuery($"DELETE FROM {ProfileTableName} WHERE ProfileID = {profileID}");
        }

        public IEnumerable<ProfileDb> GetAllProfiles()
        {
            var profiles = Database.LoadAll<ProfileDb>(CreateIfNotLoadedOptions);

            if (!profiles.Any())
            {
                var defaultProfile = CreateProfile("Default");
                return [defaultProfile];
            }

            return profiles;
        }

        public Dictionary<uint, ApplistCacheDb> GetAppsCache(IEnumerable<uint> appids)
        {
            var query = string.Join(",", appids);
            return Database.Load<ApplistCacheDb>($"WHERE AppID IN ({query})", CreateIfNotLoadedOptions).ToDictionary(x => x.AppID);
        }

        public FeaturedCacheResult GetFeaturedCache()
        {
            var games = Database.LoadAll<FeaturedCacheDb>(CreateIfNotLoadedOptions).ToList();
            return new FeaturedCacheResult(games, games.FirstOrDefault()?.CachedAt);
        }

        public void RenameProfile(int profileId, string newName)
        {
            Database.ExecuteNonQuery($"UPDATE {ProfileTableName} SET ProfileName = '{newName}' WHERE ProfileID = {profileId}");
        }

        public void SaveFeaturedCache(Dictionary<uint, string> games)
        {
            var now = DateTime.UtcNow;

            Database.BeginTransaction();
            try
            {
                Database.ExecuteNonQuery($"DELETE FROM {FeaturedCacheTableName}");

                foreach (var (appId, name) in games)
                {
                    Database.Save(new FeaturedCacheDb { AppID = appId, Name = name, CachedAt = now });
                }

                Database.Commit();
            }
            catch
            {
                Database.Rollback();
                throw;
            }
        }
    }
}