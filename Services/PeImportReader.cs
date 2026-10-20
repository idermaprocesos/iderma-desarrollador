using System.Text;
using System.Text.Json;

namespace Consola.Services;

internal static class PeImportReader
{
    public static List<(string Name, string Kind)> Read(string path)
    {
        var list = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var item in ReadPe(path))
            {
                if (seen.Add(item.Name)) list.Add(item);
            }
        }
        catch
        {
            // PE ilegible o sin acceso
        }

        try
        {
            foreach (var item in ReadDepsJson(path))
            {
                if (seen.Add(item.Name)) list.Add(item);
            }
        }
        catch
        {
            // deps.json opcional
        }

        return list;
    }

    private static IEnumerable<(string Name, string Kind)> ReadPe(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var br = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true);
        if (fs.Length < 64) yield break;
        if (br.ReadUInt16() != 0x5A4D) yield break;
        fs.Position = 0x3C;
        var pe = br.ReadInt32();
        if (pe <= 0 || pe + 24 >= fs.Length) yield break;
        fs.Position = pe;
        if (br.ReadUInt32() != 0x00004550) yield break;
        br.ReadUInt16();
        var sectionCount = br.ReadUInt16();
        fs.Position += 12;
        var optSize = br.ReadUInt16();
        br.ReadUInt16();
        var optStart = fs.Position;
        if (optSize < 2 || optStart + optSize > fs.Length) yield break;
        var magic = br.ReadUInt16();
        var pe32Plus = magic == 0x20B;
        var ddOff = pe32Plus ? 112 : 96;
        if (optSize < ddOff + 16) yield break;
        fs.Position = optStart + (pe32Plus ? 108 : 92);
        var ddCount = br.ReadUInt32();
        fs.Position = optStart + ddOff;
        uint importRva = 0, importSize = 0, delayRva = 0, delaySize = 0;
        for (var i = 0; i < ddCount && i < 16; i++)
        {
            var rva = br.ReadUInt32();
            var size = br.ReadUInt32();
            if (i == 1) { importRva = rva; importSize = size; }
            if (i == 13) { delayRva = rva; delaySize = size; }
        }

        var sections = new List<(uint Va, uint Vs, uint Ptr, uint Raw)>(sectionCount);
        fs.Position = optStart + optSize;
        for (var i = 0; i < sectionCount; i++)
        {
            if (fs.Position + 40 > fs.Length) break;
            fs.Position += 8;
            var vs = br.ReadUInt32();
            var va = br.ReadUInt32();
            var raw = br.ReadUInt32();
            var ptr = br.ReadUInt32();
            fs.Position += 16;
            sections.Add((va, vs, ptr, raw));
        }

        uint ToOff(uint rva)
        {
            foreach (var s in sections)
            {
                var span = Math.Max(s.Vs, s.Raw);
                if (span == 0) span = s.Raw;
                if (rva >= s.Va && rva < s.Va + span)
                    return s.Ptr + (rva - s.Va);
            }
            return rva;
        }

        string? ReadAscii(uint rva)
        {
            if (rva == 0) return null;
            var off = ToOff(rva);
            if (off >= (uint)fs.Length) return null;
            fs.Position = off;
            var bytes = new List<byte>(40);
            while (bytes.Count < 260)
            {
                var b = fs.ReadByte();
                if (b <= 0) break;
                bytes.Add((byte)b);
            }
            return bytes.Count == 0 ? null : Encoding.ASCII.GetString(bytes.ToArray());
        }

        if (importRva != 0 && importSize >= 20)
        {
            var off = ToOff(importRva);
            var end = Math.Min(fs.Length, off + importSize);
            fs.Position = off;
            while (fs.Position + 20 <= end)
            {
                var orig = br.ReadUInt32();
                br.ReadUInt32();
                br.ReadUInt32();
                var nameRva = br.ReadUInt32();
                var first = br.ReadUInt32();
                if (orig == 0 && nameRva == 0 && first == 0) break;
                var name = ReadAscii(nameRva);
                if (!string.IsNullOrWhiteSpace(name))
                    yield return (name, "Import PE");
                off += 20;
                fs.Position = off;
            }
        }

        if (delayRva != 0 && delaySize >= 32)
        {
            var off = ToOff(delayRva);
            var end = Math.Min(fs.Length, off + delaySize);
            fs.Position = off;
            while (fs.Position + 32 <= end)
            {
                br.ReadUInt32();
                var nameRva = br.ReadUInt32();
                fs.Position += 24;
                if (nameRva == 0) break;
                var name = ReadAscii(nameRva);
                if (!string.IsNullOrWhiteSpace(name))
                    yield return (name, "Delay-load");
                off += 32;
                fs.Position = off;
            }
        }
    }

    private static IEnumerable<(string Name, string Kind)> ReadDepsJson(string exePath)
    {
        var deps = Path.Combine(Path.GetDirectoryName(exePath) ?? "", Path.GetFileNameWithoutExtension(exePath) + ".deps.json");
        if (!File.Exists(deps)) yield break;
        using var doc = JsonDocument.Parse(File.ReadAllText(deps));
        if (!doc.RootElement.TryGetProperty("libraries", out var libs)) yield break;
        foreach (var lib in libs.EnumerateObject())
        {
            var id = lib.Name;
            if (id.StartsWith("runtimepack.", StringComparison.OrdinalIgnoreCase)) continue;
            if (id.Contains("Microsoft.NETCore.App.Runtime", StringComparison.OrdinalIgnoreCase)) continue;
            if (id.Contains("Microsoft.WindowsDesktop.App.Runtime", StringComparison.OrdinalIgnoreCase)) continue;
            var name = id.Split('/')[0];
            if (string.Equals(name, Path.GetFileNameWithoutExtension(exePath), StringComparison.OrdinalIgnoreCase))
                continue;
            yield return (name + ".dll", ".NET");
        }
    }
}
