using SqlNado;

namespace BV6Tools.Services.Database.Models
{
    [SQLiteTable(Name = "Ticket")]
    public class TicketDb
    {
        [SQLiteColumn(IsPrimaryKey = true)]
        public uint AppId { get; set; }
        public byte[]? EncryptedTicketBytes { get; set; }
        public byte[]? AppTicketBytes { get; set; }
        public ulong OwnerID { get; set; }
    }
}
