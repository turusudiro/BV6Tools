using SqlNado;

namespace BV6Tools.Services.Database.Models
{
    [SQLiteTable(Name = "Appids")]
    public class AppDb
    {
        [SQLiteColumn(IsPrimaryKey = true)]
        public uint AppID { get; set; }

        [SQLiteColumn(IsPrimaryKey = true)]
        public int ProfileID { get; set; }
    }
}
