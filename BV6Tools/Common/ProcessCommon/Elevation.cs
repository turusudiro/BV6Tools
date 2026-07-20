using System.Security.Principal;

namespace ProcessCommon
{
    internal class Elevation
    {
        private static readonly Lazy<bool> _isElevated = new(() =>
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        });

        public static bool IsRunningAsAdmin => _isElevated.Value;
    }
}