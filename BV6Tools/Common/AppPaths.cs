using System.IO;

namespace AppPathsCommon
{
    public static class AppPaths
    {
        #region Data Path

        /// <summary>
        /// Base directory where the executable is running
        /// </summary>
        public static readonly string BasePath = AppDomain.CurrentDomain.BaseDirectory;

        /// <summary>
        /// <see cref="BasePath"/>\<c>Data</c>
        /// </summary>
        public static readonly string DataPath = Path.Combine(BasePath, "Data");

        #endregion Data Path

        /// <summary>
        /// <see cref="BasePath"/>\<c>Data\.db</c>
        /// </summary>
        public static readonly string DbPath = Path.Combine(DataPath, ".db");

        /// <summary>
        /// <see cref="BasePath"/>\<c>Data\GreenLuma</c>
        /// </summary>
        public static readonly string GLPath = Path.Combine(DataPath, "GreenLuma");

        /// <summary>
        /// <see cref="BasePath"/>\<c>Data\Images\fallback.jpg</c>
        /// </summary>
        public static readonly string ImageFallbackPath = Path.Combine(DataPath, "Images", "fallback.jpg");

        /// <summary>
        /// <see cref="BasePath"/>\<c>Data\Images</c>
        /// </summary>
        public static readonly string ImagesPath = Path.Combine(DataPath, "Images");

        /// <summary>
        /// <see cref="BasePath"/>\<c>Data\lua</c>
        /// </summary>
        public static readonly string LuaPath = Path.Combine(DataPath, "lua");

        /// <summary>
        /// <see cref="BasePath"/>\<c>Data\manifest</c>
        /// </summary>
        public static readonly string ManifestPath = Path.Combine(DataPath, "manifest");

        /// <summary>
        /// <see cref="BasePath"/>\<c>Data\OpenSteamTool</c>
        /// </summary>
        public static readonly string OpenSteamToolPath = Path.Combine(DataPath, "OpenSteamTool");

        /// <summary>
        /// <see cref="BasePath"/>\<c>Data\steamprocess.json</c>
        /// </summary>
        public static readonly string SteamProcess = Path.Combine(DataPath, "steamprocess.json");

        /// <summary>
        /// <see cref="BasePath"/>\<c>Data\ST</c>
        /// </summary>
        public static readonly string STPath = Path.Combine(DataPath, "ST");

        static AppPaths()
        {
            Directory.CreateDirectory(DataPath);
        }
    }
}