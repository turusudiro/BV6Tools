using SqlNado;

namespace BV6Tools.Services.Database.Models
{
    [SQLiteTable(Name = "Profiles")]
    public class ProfileDb
    {
        [SQLiteColumn(IsPrimaryKey = true, AutoIncrements = true)]
        public int ProfileID { get; set; }

        public string ProfileName { get; set; } = string.Empty;
    }
}
