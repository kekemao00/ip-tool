using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace IP_UpdateTest.Core.TerminalProxy
{
    /// <summary>
    /// 一个本机端口说的是哪种代理协议
    /// </summary>
    public sealed class PortProbeResult
    {
        public bool Listening { get; set; }

        /// <summary>
        /// 能当 HTTP 代理用（应答 CONNECT）
        /// </summary>
        public bool Http { get; set; }

        /// <summary>
        /// 能当 SOCKS5 代理用（应答无认证握手）
        /// </summary>
        public bool Socks5 { get; set; }
    }

    /// <summary>
    /// 本机监听中的 TCP 端口及其所属进程
    /// </summary>
    public sealed class PortListener
    {
        public IPAddress Address { get; set; }
        public int Port { get; set; }
        public int ProcessId { get; set; }
    }

    /// <summary>
    /// 探测代理端口：能否连接、是 HTTP 还是 SOCKS5（混合端口两者都是）；经代理实际访问外网
    /// </summary>
    public static class ProxyPortProbe
    {
        private const string TunnelHost = "www.gstatic.com";

        public static async Task<bool> CanConnectAsync(string host, int port, int timeoutMilliseconds = 600)
        {
            using (var client = new TcpClient())
            {
                try
                {
                    Task connect = client.ConnectAsync(host, port);
                    if (await Task.WhenAny(connect, Task.Delay(timeoutMilliseconds)) != connect) return false;
                    await connect;
                    return client.Connected;
                }
                catch (Exception ex) when (ex is SocketException || ex is ObjectDisposedException || ex is ArgumentException)
                {
                    return false;
                }
            }
        }

        public static async Task<PortProbeResult> ProbeAsync(string host, int port)
        {
            var result = new PortProbeResult { Listening = await CanConnectAsync(host, port) };
            if (!result.Listening) return result;
            Task<byte[]> http = ExchangeAsync(host, port, Encoding.ASCII.GetBytes($"CONNECT {TunnelHost}:443 HTTP/1.1\r\nHost: {TunnelHost}:443\r\n\r\n"), 12);
            Task<byte[]> socks = ExchangeAsync(host, port, new byte[] { 5, 1, 0 }, 2);
            result.Http = IsHttpResponse(await http);
            result.Socks5 = IsSocks5Greeting(await socks);
            return result;
        }

        /// <summary>
        /// 代理对 CONNECT 的应答：200 隧道已建立、407 要认证、502 / 503 / 504 代理连不上目标。
        /// 网页界面和控制接口（如 v2rayA 的 2017、Clash 的 9090）会回 400 / 401 / 404 / 405，不算代理
        /// </summary>
        internal static bool IsHttpResponse(byte[] data)
        {
            if (data == null || data.Length < 12) return false;
            string head = Encoding.ASCII.GetString(data, 0, 12); // “HTTP/1.1 200”
            int status;
            if (!head.StartsWith("HTTP/1.", StringComparison.Ordinal) || head[8] != ' '
                || !int.TryParse(head.Substring(9, 3), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out status))
                return false;
            return status == 200 || status == 407 || status == 502 || status == 503 || status == 504;
        }

        /// <summary>
        /// SOCKS5 对“无认证”握手的应答：版本 5、方法 0
        /// </summary>
        internal static bool IsSocks5Greeting(byte[] data)
        {
            return data != null && data.Length >= 2 && data[0] == 5 && data[1] == 0;
        }

        private static async Task<byte[]> ExchangeAsync(string host, int port, byte[] request, int want, int timeoutMilliseconds = 1500)
        {
            using (var client = new TcpClient())
            {
                try
                {
                    Task connect = client.ConnectAsync(host, port);
                    if (await Task.WhenAny(connect, Task.Delay(timeoutMilliseconds)) != connect) return null;
                    await connect;
                    NetworkStream stream = client.GetStream();
                    await stream.WriteAsync(request, 0, request.Length);
                    var buffer = new byte[want];
                    int read = 0;
                    var watch = Stopwatch.StartNew();
                    while (read < want)
                    {
                        int left = timeoutMilliseconds - (int)watch.ElapsedMilliseconds;
                        if (left <= 0) break;
                        Task<int> task = stream.ReadAsync(buffer, read, want - read);
                        if (await Task.WhenAny(task, Task.Delay(left)) != task) break;
                        int n = await task;
                        if (n <= 0) break;
                        read += n;
                    }
                    if (read == 0) return null;
                    Array.Resize(ref buffer, read);
                    return buffer;
                }
                catch (Exception ex) when (ex is SocketException || ex is IOException || ex is ObjectDisposedException || ex is InvalidOperationException)
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// 经代理访问外网（HTTPS，和 git、curl 走的 CONNECT 隧道一样），返回耗时毫秒；失败返回 null
        /// </summary>
        public static Task<long?> TunnelAsync(string proxyUrl, int timeoutMilliseconds = 6000)
        {
            return Task.Run(() =>
            {
                Uri proxy;
                if (!Uri.TryCreate(proxyUrl, UriKind.Absolute, out proxy) || !proxy.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return (long?)null;
                try
                {
                    var request = (HttpWebRequest)WebRequest.Create("https://" + TunnelHost + "/generate_204");
                    request.Proxy = new WebProxy(proxy);
                    request.Timeout = timeoutMilliseconds;
                    request.ReadWriteTimeout = timeoutMilliseconds;
                    request.AllowAutoRedirect = false;
                    var watch = Stopwatch.StartNew();
                    using (var response = (HttpWebResponse)request.GetResponse())
                    {
                        return (int)response.StatusCode < 400 ? watch.ElapsedMilliseconds : (long?)null;
                    }
                }
                catch (Exception ex) when (ex is WebException || ex is IOException || ex is InvalidOperationException || ex is NotSupportedException)
                {
                    return null;
                }
            });
        }

        #region 监听端口表

        private const int AfInet = 2;
        private const int AfInet6 = 23;
        private const int TcpTableOwnerPidListener = 3;

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);

        /// <summary>
        /// 本机所有监听中的 TCP 端口（IPv4 和 IPv6）；读不到时返回空表。
        /// mihomo、xray 等 Go 程序“允许局域网”时监听 [::]，这种双栈端口只出现在 IPv6 表里
        /// </summary>
        public static List<PortListener> Listeners()
        {
            var list = new List<PortListener>();
            // MIB_TCPROW_OWNER_PID（24 字节）：state、localAddr、localPort、remoteAddr、remotePort、owningPid，各 4 字节
            ReadTable(list, AfInet, 24, row => new PortListener
            {
                Address = new IPAddress((uint)Marshal.ReadInt32(row, 4)),
                Port = NetworkPort((uint)Marshal.ReadInt32(row, 8)),
                ProcessId = Marshal.ReadInt32(row, 20)
            });
            // MIB_TCP6ROW_OWNER_PID（56 字节）：localAddr[16]、localScopeId、localPort、remoteAddr[16]、remoteScopeId、remotePort、state、owningPid
            ReadTable(list, AfInet6, 56, row =>
            {
                var address = new byte[16];
                Marshal.Copy(row, address, 0, 16);
                return new PortListener
                {
                    Address = new IPAddress(address),
                    Port = NetworkPort((uint)Marshal.ReadInt32(row, 20)),
                    ProcessId = Marshal.ReadInt32(row, 52)
                };
            });
            return list;
        }

        private static void ReadTable(List<PortListener> list, int family, int rowSize, Func<IntPtr, PortListener> read)
        {
            int size = 0;
            try
            {
                GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, TcpTableOwnerPidListener, 0);
                if (size <= 0) return;
                IntPtr buffer = Marshal.AllocHGlobal(size);
                try
                {
                    if (GetExtendedTcpTable(buffer, ref size, false, family, TcpTableOwnerPidListener, 0) != 0) return;
                    int count = Marshal.ReadInt32(buffer);
                    for (int i = 0; i < count; i++) list.Add(read(buffer + 4 + i * rowSize));
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
            }
        }

        /// <summary>
        /// 表中的端口是网络字节序，放在低 16 位
        /// </summary>
        internal static int NetworkPort(uint value)
        {
            return (int)(((value & 0xFF) << 8) | ((value >> 8) & 0xFF));
        }

        #endregion
    }
}
