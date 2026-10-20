using System.Runtime.InteropServices;

namespace Consola.Helpers;

internal static class CrashLog
{
    public static string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "consola-boot.log");

    public static void Write(string message)
    {
        try
        {
            File.AppendAllText(Path, $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}");
        }
        catch { /* ignore */ }
    }

    public static void Write(Exception ex) => Write(ex.ToString());

    public static void Alert(string title, string message)
    {
        Write($"{title}: {message}");
        MessageBoxW(IntPtr.Zero, message, title, 0x00000010);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
