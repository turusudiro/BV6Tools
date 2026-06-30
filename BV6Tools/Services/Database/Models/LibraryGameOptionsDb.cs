using BV6Tools.Services.Injector;
using GreenLumaCommon;
using SqlNado;

namespace BV6Tools.Services.Database.Models
{
    [SQLiteTable(Name = "LibraryGameOptions")]
    public class LibraryGameOptionsDb
    {
        [SQLiteColumn(IsPrimaryKey = true)]
        public uint AppId { get; set; }

        public bool Base { get; set; }
        public bool DLC { get; set; }
        public GreenLumaMode GreenLumaMode { get; set; }
        public bool OnlineFix { get; set; }
        public ProcessMode ProcessMode { get; set; } = ProcessMode.GreenLumaStealth;

        public LibraryGameOptionsDb Clone()
        {
            return new()
            {
                AppId = AppId,
                Base = Base,
                DLC = DLC,
                ProcessMode = ProcessMode,
                GreenLumaMode = GreenLumaMode,
                OnlineFix = OnlineFix
            };
        }
    }
}
