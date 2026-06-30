using SqlNado;

namespace BV6Tools.Services.Database.Models
{
    [SQLiteTable(Name = "Library")]
    public class LibraryDb
    {
        [SQLiteColumn(IsPrimaryKey = true)]
        public uint AppId { get; set; }

        public bool IsEnabled { get; set; }
        public string? Name { get; set; }
        public uint Parent { get; set; }
    }
}