
using SqlNado;

namespace BV6Tools.Services.Database.Models
{
    [SQLiteTable(Name = "Cache")]
    public class ApplistCacheDb
    {
        [SQLiteColumn(IsPrimaryKey = true)]
        public uint AppID { get; set; }

        public required string Name { get; set; }
    }
}
