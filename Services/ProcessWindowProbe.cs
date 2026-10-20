using Consola.Helpers;
using Consola.Models;

namespace Consola.Services;

internal static class ProcessWindowProbe
{
    public static List<WindowInfoRow> ForPid(int pid)
    {
        var list = new List<WindowInfoRow>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out var owner);
            if (owner != (uint)pid) return true;
            Add(hwnd, list);
            NativeMethods.EnumChildWindows(hwnd, (child, _) =>
            {
                Add(child, list);
                return list.Count < 80;
            }, IntPtr.Zero);
            return list.Count < 80;
        }, IntPtr.Zero);
        return list
            .GroupBy(w => w.Title + "|" + w.ClassName)
            .Select(g => g.First())
            .Take(80)
            .ToList();
    }

    private static void Add(IntPtr hwnd, List<WindowInfoRow> list)
    {
        var titleBuf = new char[512];
        var classBuf = new char[256];
        var titleLen = NativeMethods.GetWindowText(hwnd, titleBuf, titleBuf.Length);
        var classLen = NativeMethods.GetClassName(hwnd, classBuf, classBuf.Length);
        var title = titleLen > 0 ? new string(titleBuf, 0, titleLen).Trim() : "";
        if (title.Length == 0) return;
        list.Add(new WindowInfoRow
        {
            Title = title,
            ClassName = classLen > 0 ? new string(classBuf, 0, classLen) : "",
            Visible = NativeMethods.IsWindowVisible(hwnd)
        });
    }
}
