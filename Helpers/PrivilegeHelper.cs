using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Consola.Helpers;

internal static class PrivilegeHelper
{
    public static bool IsElevated
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    public static bool TryEnableDebugPrivilege()
    {
        if (!OpenCurrentToken(out var token)) return false;
        try
        {
            if (!NativeMethods.LookupPrivilegeValue(null, "SeDebugPrivilege", out var luid))
                return false;

            var tp = new NativeMethods.TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = NativeMethods.SE_PRIVILEGE_ENABLED
            };
            return NativeMethods.AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            NativeMethods.CloseHandle(token);
        }
    }

    public static void RestartAsAdministrator()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path)) return;

        var start = new System.Diagnostics.ProcessStartInfo(path)
        {
            UseShellExecute = true,
            Verb = "runas"
        };
        System.Diagnostics.Process.Start(start);
    }

    private static bool OpenCurrentToken(out IntPtr token)
    {
        return NativeMethods.OpenProcessToken(NativeMethods.GetCurrentProcess(),
            NativeMethods.TOKEN_ADJUST_PRIVILEGES | NativeMethods.TOKEN_QUERY, out token);
    }
}
