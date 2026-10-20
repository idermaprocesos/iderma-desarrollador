using System.Diagnostics;
using Consola.Helpers;
using Microsoft.Win32.SafeHandles;

namespace Consola.Services;

internal static class MiniDumpService
{
    public static string DumpFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Consola", "dumps");

    public static string WriteDump(int pid, bool fullMemory)
    {
        Directory.CreateDirectory(DumpFolder);
        var name = $"pid-{pid}-{DateTime.Now:yyyyMMdd-HHmmss}.dmp";
        var path = Path.Combine(DumpFolder, name);

        var access = NativeMethods.PROCESS_QUERY_INFORMATION | NativeMethods.PROCESS_VM_READ | NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION;
        var handle = NativeMethods.OpenProcess(access, false, pid);
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException("No se pudo abrir el proceso. Ejecuta Consola como administrador o elige un proceso accesible.");

        try
        {
            using var file = File.Create(path);
            uint type = NativeMethods.MiniDumpWithThreadInfo
                        | NativeMethods.MiniDumpWithHandleData
                        | NativeMethods.MiniDumpWithUnloadedModules
                        | NativeMethods.MiniDumpWithFullMemoryInfo;
            if (fullMemory) type |= NativeMethods.MiniDumpWithFullMemory;
            else type |= NativeMethods.MiniDumpNormal | NativeMethods.MiniDumpWithDataSegs;

            if (!NativeMethods.MiniDumpWriteDump(handle, (uint)pid, file.SafeFileHandle, type, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero))
            {
                var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"MiniDumpWriteDump falló (Win32 {error}).");
            }
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }

        return path;
    }

    public static void OpenFolder()
    {
        Directory.CreateDirectory(DumpFolder);
        Process.Start(new ProcessStartInfo("explorer.exe", DumpFolder) { UseShellExecute = true });
    }
}
