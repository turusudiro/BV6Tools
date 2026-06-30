using Microsoft.Win32;
using System.IO;
using System.Text.RegularExpressions;

namespace FileSystemCommon
{
    public static partial class FileSystem
    {
        private const string longPathPrefix = @"\\?\";
        private const string longPathUncPrefix = @"\\?\UNC\";

        public static void Copy(string sourceFileName, string destFileName, bool overwrite = true)
        {
            sourceFileName = FixPathLength(sourceFileName);
            destFileName = FixPathLength(destFileName);

            string destDir = Path.GetDirectoryName(destFileName) ?? throw new DirectoryNotFoundException($"Directory not found for {destFileName}");

            Directory.CreateDirectory(destDir);
            File.Copy(sourceFileName, destFileName, overwrite);
        }

        public static string FixPathLength(string path)
        {
            // Relative paths don't support long paths
            // https://docs.microsoft.com/en-us/windows/win32/fileio/maximum-file-path-limitation?tabs=cmd
            if (!IsFullPath(path))
            {
                return path;
            }

            if (path.Length >= 258 && !path.StartsWith(longPathPrefix))
            {
                if (path.StartsWith(@"\\"))
                {
                    return string.Concat(longPathUncPrefix, path.AsSpan(2));
                }
                else
                {
                    return longPathPrefix + path;
                }
            }

            return path;
        }

        public static string GetExactPathName(string pathName)
        {
            if (!(File.Exists(pathName) || Directory.Exists(pathName)))
                return pathName;

            var di = new DirectoryInfo(pathName);

            if (di.Parent != null)
            {
                return Path.Combine(
                    GetExactPathName(di.Parent.FullName),
                    di.Parent.GetFileSystemInfos(di.Name)[0].Name);
            }
            else
            {
                return di.Name.ToUpper();
            }
        }

        public static bool IsDeveloperModeEnabled()
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock");
            return key?.GetValue("AllowDevelopmentWithoutDevLicense") is 1;
        }

        // Source - https://stackoverflow.com/a/326153
        // Posted by Yona, modified by community. See post 'Timeline' for change history
        // Retrieved 2026-05-26, License - CC BY-SA 3.0
        public static bool IsFullPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            return PathRootedRegex().IsMatch(path);
        }

        [GeneratedRegex(@"^([a-zA-Z]:\\|\\\\)")]
        private static partial Regex PathRootedRegex();
    }
}