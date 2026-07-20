using System.IO;
using System.Reflection;

namespace UninstallerCommon
{
    internal static class Uninstaller
    {
        private const string ResourceName = "BV6Tools.Scripts.uninstall.bat";

        public static async Task EnsureUninstallerExists(CancellationToken cancellationToken)
        {
            string dir = AppPathsCommon.AppPaths.BasePath;
            string filePath = Path.Combine(dir, "uninstall.bat");

            if (File.Exists(filePath))
                return;

            var assembly = Assembly.GetExecutingAssembly();
            using var resourceStream = assembly.GetManifestResourceStream(ResourceName);
            if (resourceStream == null)
                return;

            using var fileStream = File.Create(filePath);
            await resourceStream.CopyToAsync(fileStream, cancellationToken);
        }
    }
}