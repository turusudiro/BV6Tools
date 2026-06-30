using SqlNado;

namespace BV6Tools.Services.Database.Models
{
    [SQLiteTable(Name = "FeaturedCache")]
    public class FeaturedCacheDb
    {
        [SQLiteColumn(IsPrimaryKey = true)]
        public uint AppID { get; set; }

        public DateTime CachedAt { get; set; }
        public required string Name { get; set; }
    }
}
