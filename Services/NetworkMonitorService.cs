using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Consola.Helpers;
using Consola.Models;

namespace Consola.Services;

internal static class NetworkMonitorService
{
    public static List<NetConnectionRow> ForPid(int pid)
    {
        var list = new List<NetConnectionRow>();
        ReadTcp(pid, NativeMethods.AF_INET, list);
        ReadTcp(pid, NativeMethods.AF_INET6, list);
        ReadUdp(pid, NativeMethods.AF_INET, list);
        ReadUdp(pid, NativeMethods.AF_INET6, list);
        return list
            .OrderByDescending(c => c.IsInternet)
            .ThenBy(c => c.Protocol)
            .ThenBy(c => c.Remote)
            .ToList();
    }

    public static (int internet, int lan, int listen) Counts(IReadOnlyList<NetConnectionRow> rows)
    {
        var internet = rows.Count(r => r.IsInternet);
        var listen = rows.Count(r => r.State is "LISTEN" or "UDP");
        var lan = rows.Count(r => r.Scope is "LAN" or "Link-local");
        return (internet, lan, listen);
    }

    private static void ReadTcp(int pid, int family, List<NetConnectionRow> list)
    {
        var buf = Query(family, tcp: true);
        if (buf == IntPtr.Zero) return;
        try
        {
            var count = Marshal.ReadInt32(buf);
            var offset = 4;
            var rowSize = family == NativeMethods.AF_INET6 ? 56 : 24;
            for (var i = 0; i < count; i++)
            {
                var row = buf + offset + i * rowSize;
                if (family == NativeMethods.AF_INET)
                {
                    var state = Marshal.ReadInt32(row);
                    var localAddr = ReadUInt(row, 4);
                    var localPort = Port(ReadUInt(row, 8));
                    var remoteAddr = ReadUInt(row, 12);
                    var remotePort = Port(ReadUInt(row, 16));
                    var owner = Marshal.ReadInt32(row, 20);
                    if (owner != pid) continue;
                    list.Add(Make("TCP", V4(localAddr), localPort, V4(remoteAddr), remotePort, TcpState(state)));
                }
                else
                {
                    var local = ReadIp6(row);
                    var localPort = Port(ReadUInt(row, 20));
                    var remote = ReadIp6(row + 24);
                    var remotePort = Port(ReadUInt(row, 44));
                    var state = Marshal.ReadInt32(row, 48);
                    var owner = Marshal.ReadInt32(row, 52);
                    if (owner != pid) continue;
                    list.Add(Make("TCP6", local, localPort, remote, remotePort, TcpState(state)));
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private static void ReadUdp(int pid, int family, List<NetConnectionRow> list)
    {
        var buf = Query(family, tcp: false);
        if (buf == IntPtr.Zero) return;
        try
        {
            var count = Marshal.ReadInt32(buf);
            var offset = 4;
            var rowSize = family == NativeMethods.AF_INET6 ? 28 : 12;
            for (var i = 0; i < count; i++)
            {
                var row = buf + offset + i * rowSize;
                if (family == NativeMethods.AF_INET)
                {
                    var localAddr = ReadUInt(row, 0);
                    var localPort = Port(ReadUInt(row, 4));
                    var owner = Marshal.ReadInt32(row, 8);
                    if (owner != pid) continue;
                    list.Add(Make("UDP", V4(localAddr), localPort, IPAddress.Any, 0, "UDP"));
                }
                else
                {
                    var local = ReadIp6(row);
                    var localPort = Port(ReadUInt(row, 20));
                    var owner = Marshal.ReadInt32(row, 24);
                    if (owner != pid) continue;
                    list.Add(Make("UDP6", local, localPort, IPAddress.IPv6Any, 0, "UDP"));
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private static IntPtr Query(int family, bool tcp)
    {
        var size = 0;
        if (tcp)
            NativeMethods.GetExtendedTcpTable(IntPtr.Zero, ref size, true, family, NativeMethods.TCP_TABLE_OWNER_PID_ALL, 0);
        else
            NativeMethods.GetExtendedUdpTable(IntPtr.Zero, ref size, true, family, NativeMethods.UDP_TABLE_OWNER_PID, 0);
        if (size <= 4) return IntPtr.Zero;
        var buf = Marshal.AllocHGlobal(size);
        var err = tcp
            ? NativeMethods.GetExtendedTcpTable(buf, ref size, true, family, NativeMethods.TCP_TABLE_OWNER_PID_ALL, 0)
            : NativeMethods.GetExtendedUdpTable(buf, ref size, true, family, NativeMethods.UDP_TABLE_OWNER_PID, 0);
        if (err == 0) return buf;
        Marshal.FreeHGlobal(buf);
        return IntPtr.Zero;
    }

    private static NetConnectionRow Make(string proto, IPAddress local, int localPort, IPAddress remote, int remotePort, string state)
    {
        var internet = IsInternet(remote, state);
        var scope = ScopeOf(remote, state);
        var hint = PortHint(remotePort != 0 ? remotePort : localPort);
        return new NetConnectionRow
        {
            Protocol = proto,
            Local = FormatEnd(local, localPort),
            Remote = remotePort == 0 && (remote.Equals(IPAddress.Any) || remote.Equals(IPAddress.IPv6Any))
                ? "—"
                : FormatEnd(remote, remotePort),
            State = state,
            Scope = scope,
            Hint = hint,
            IsInternet = internet
        };
    }

    private static string FormatEnd(IPAddress ip, int port)
    {
        var host = ip.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{ip}]" : ip.ToString();
        return port == 0 ? host : $"{host}:{port}";
    }

    private static bool IsInternet(IPAddress ip, string state)
    {
        if (state is "LISTEN" or "UDP") return false;
        return ScopeOf(ip, state) == "Internet";
    }

    private static string ScopeOf(IPAddress ip, string state)
    {
        if (IPAddress.IsLoopback(ip)) return "Loopback";
        if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.None))
            return state is "LISTEN" or "UDP" ? "Escucha" : "—";
        if (ip.IsIPv6LinkLocal) return "Link-local";
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            if (b[0] == 10) return "LAN";
            if (b[0] == 172 && b[1] is >= 16 and <= 31) return "LAN";
            if (b[0] == 192 && b[1] == 168) return "LAN";
            if (b[0] == 169 && b[1] == 254) return "Link-local";
            if (b[0] == 127) return "Loopback";
        }
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = ip.GetAddressBytes();
            if (b[0] == 0xFC || b[0] == 0xFD) return "LAN";
            if (b[0] == 0xFE && (b[1] & 0xC0) == 0x80) return "Link-local";
        }
        return "Internet";
    }

    private static string TcpState(int state) => state switch
    {
        1 => "CLOSED",
        2 => "LISTEN",
        3 => "SYN_SENT",
        4 => "SYN_RCVD",
        5 => "ESTABLISHED",
        6 => "FIN_WAIT1",
        7 => "FIN_WAIT2",
        8 => "CLOSE_WAIT",
        9 => "CLOSING",
        10 => "LAST_ACK",
        11 => "TIME_WAIT",
        12 => "DELETE_TCB",
        _ => state.ToString()
    };

    private static string PortHint(int port) => port switch
    {
        80 => "HTTP",
        443 => "HTTPS",
        53 => "DNS",
        22 => "SSH",
        25 or 587 => "SMTP",
        110 => "POP3",
        143 => "IMAP",
        993 => "IMAPS",
        995 => "POP3S",
        123 => "NTP",
        135 => "RPC",
        139 => "NetBIOS",
        445 => "SMB",
        3306 => "MySQL",
        3389 => "RDP",
        5432 => "PostgreSQL",
        5353 => "mDNS",
        5228 => "Google",
        8080 => "HTTP alt",
        8443 => "HTTPS alt",
        27017 => "MongoDB",
        _ => ""
    };

    private static int Port(uint value)
    {
        var raw = (ushort)(value & 0xFFFF);
        return (ushort)IPAddress.NetworkToHostOrder((short)raw);
    }

    private static uint ReadUInt(IntPtr ptr, int offset) => unchecked((uint)Marshal.ReadInt32(ptr, offset));

    private static IPAddress V4(uint addr) => new((long)addr);

    private static IPAddress ReadIp6(IntPtr ptr)
    {
        var bytes = new byte[16];
        Marshal.Copy(ptr, bytes, 0, 16);
        return new IPAddress(bytes);
    }
}
