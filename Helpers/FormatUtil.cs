using System.Globalization;

namespace Consola.Helpers;

internal static class FormatUtil
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    public static string Bytes(long value)
    {
        var abs = Math.Abs(value);
        return abs switch
        {
            >= 1L << 40 => $"{value / (double)(1L << 40):0.00} TB",
            >= 1L << 30 => $"{value / (double)(1L << 30):0.00} GB",
            >= 1L << 20 => $"{value / (double)(1L << 20):0.0} MB",
            >= 1L << 10 => $"{value / (double)(1L << 10):0} KB",
            _ => $"{value} B"
        };
    }

    public static string Bytes(ulong value) => Bytes(unchecked((long)value));

    public static string Percent(double value) => $"{value.ToString("0.0", Es)} %";

    public static string Rate(double bytesPerSecond) => $"{Bytes((long)bytesPerSecond)}/s";

    public static string Duration(TimeSpan value)
    {
        if (value.TotalDays >= 1) return $"{(int)value.TotalDays}d {value.Hours:00}:{value.Minutes:00}:{value.Seconds:00}";
        if (value.TotalHours >= 1) return value.ToString(@"hh\:mm\:ss");
        return value.ToString(@"mm\:ss");
    }

    public static string Time(DateTimeOffset value) => value.ToLocalTime().ToString("HH:mm:ss.fff");

    public static string DateTime(DateTime value) => value.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");

    public static string Hex(ulong value) => "0x" + value.ToString("X");

    public static string Hex(IntPtr value) => "0x" + value.ToInt64().ToString("X");
}
