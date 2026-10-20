using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Consola.Helpers;
using Consola.Models;

namespace Consola.Services;

internal static class ProcessConsoleReader
{
    private static readonly object Gate = new();

    public static (string status, string text) Read(int pid)
    {
        lock (Gate)
        {
            NativeMethods.FreeConsole();
            if (!NativeMethods.AttachConsole(pid))
            {
                var err = Marshal.GetLastWin32Error();
                return (Describe(err), "");
            }

            try
            {
                var handle = NativeMethods.CreateFile("CONOUT$", NativeMethods.GENERIC_READ,
                    NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                    IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);
                if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                    return ("Consola adjunta, pero no se pudo abrir CONOUT$.", "");

                try
                {
                    if (!NativeMethods.GetConsoleScreenBufferInfo(handle, out var info))
                        return ("Hay consola, pero no se pudo leer el búfer.", "");

                    var left = info.srWindow.Left;
                    var top = info.srWindow.Top;
                    var width = Math.Max(1, info.srWindow.Right - left + 1);
                    var height = Math.Max(1, info.srWindow.Bottom - top + 1);
                    width = Math.Min(width, 240);
                    height = Math.Min(height, 80);
                    var sb = new StringBuilder(width * height);
                    var row = new char[width];
                    for (short y = 0; y < height; y++)
                    {
                        var coord = new NativeMethods.COORD { X = left, Y = (short)(top + y) };
                        if (!NativeMethods.ReadConsoleOutputCharacter(handle, row, (uint)width, coord, out var read) || read == 0)
                            continue;
                        var line = new string(row, 0, (int)read).TrimEnd();
                        if (line.Length > 0) sb.AppendLine(line);
                    }

                    var text = sb.ToString().Trim();
                    return text.Length == 0
                        ? ("Consola vacía o sin texto visible.", "")
                        : ("Consola Win32 leída.", text);
                }
                finally
                {
                    NativeMethods.CloseHandle(handle);
                }
            }
            finally
            {
                NativeMethods.FreeConsole();
            }
        }
    }

    private static string Describe(int error) => error switch
    {
        5 => "Acceso denegado al adjuntar la consola. Prueba como administrador.",
        6 or 87 => "Este proceso no tiene consola Win32 (es típico en programas con ventana).",
        31 => "No hay consola asociada a este PID.",
        _ => $"No se pudo adjuntar la consola (Win32 {error}: {new Win32Exception(error).Message})."
    };
}
