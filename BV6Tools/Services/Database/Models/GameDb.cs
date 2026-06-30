using SqlNado;

namespace BV6Tools.Services.Database.Models
{
    [SQLiteTable(Name = "Games")]
    public class GameDb : AppDb
    {
        [SQLiteColumn(IsPrimaryKey = true)]
        public required string ManagerType { get; set; }
    }
}
