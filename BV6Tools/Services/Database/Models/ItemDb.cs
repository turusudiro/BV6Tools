using SqlNado;

namespace BV6Tools.Services.Database.Models
{
    [SQLiteTable(Name = "Items")]
    public class ItemDb : GameDb
    {
        [SQLiteColumn(IsPrimaryKey = true)]
        public uint ParentAppID { get; set; }
    }
}
