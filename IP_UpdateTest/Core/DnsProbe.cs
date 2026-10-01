using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace IP_UpdateTest.Core
{
    /// <summary>
    /// 一个 DNS 服务器的测速结果
    /// </summary>
    public sealed class DnsBenchmarkResult
    {
        public IPAddress Server { get; set; }

        /// <summary>
        /// 成功查询的耗时中位数（毫秒），全部失败时为 null
        /// </summary>
        public long? MedianMilliseconds { get; set; }

        public int Succeeded { get; set; }

        public int Attempts { get; set; }
    }

    /// <summary>
    /// 直接向 DNS 服务器发送查询（UDP 53 端口），不经过系统解析缓存，
    /// 能反映服务器本身是否可用及延迟。用于 DNS 测速和网络诊断。
    /// </summary>
    public static class DnsProbe
    {
        /// <summary>
        /// 测速时查询的域名：大型站点，公共 DNS 基本都有缓存，测到的主要是网络延迟
        /// </summary>
        public static readonly string[] ProbeNames = { "www.baidu.com", "www.qq.com", "www.taobao.com" };

        private static readonly Random IdSource = new Random();

        /// <summary>
        /// 构造要求递归解析的 A 记录查询报文
        /// </summary>
        public static byte[] BuildQuery(ushort id, string name)
        {
            // 报头：ID、标志 0x0100（RD=1）、问题数 1、其余计数 0
            var packet = new List<byte> { (byte)(id >> 8), (byte)id, 0x01, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            foreach (string label in name.TrimEnd('.').Split('.'))
            {
                byte[] bytes = Encoding.ASCII.GetBytes(label);
                if (bytes.Length == 0 || bytes.Length > 63) throw new ArgumentException("域名格式不正确", nameof(name));
                packet.Add((byte)bytes.Length);
                packet.AddRange(bytes);
            }
            // 根标签、类型 A（1）、类 IN（1）
            packet.AddRange(new byte[] { 0x00, 0x00, 0x01, 0x00, 0x01 });
            return packet.ToArray();
        }

        /// <summary>
        /// 是否为对应查询的有效应答：ID 一致、QR=1，且 RCODE 为 0（有结果）或 3（域名不存在，服务器同样正常）
        /// </summary>
        public static bool IsResponseTo(byte[] response, ushort id)
        {
            if (response == null || response.Length < 12) return false;
            bool sameId = response[0] == (byte)(id >> 8) && response[1] == (byte)id;
            bool isResponse = (response[2] & 0x80) != 0;
            int rcode = response[3] & 0x0F;
            return sameId && isResponse && (rcode == 0 || rcode == 3);
        }

        /// <summary>
        /// 查询一次，返回往返时间（毫秒）；超时或出错返回 null
        /// </summary>
        public static async Task<long?> QueryAsync(IPAddress server, string name, int timeoutMilliseconds)
        {
            ushort id;
            lock (IdSource)
            {
                id = (ushort)IdSource.Next(ushort.MaxValue + 1);
            }
            byte[] query = BuildQuery(id, name);

            using (var client = new UdpClient(server.AddressFamily))
            {
                Stopwatch watch = Stopwatch.StartNew();
                try
                {
                    client.Connect(server, 53);
                    await client.SendAsync(query, query.Length).ConfigureAwait(false);
                    while (true)
                    {
                        int remaining = timeoutMilliseconds - (int)watch.ElapsedMilliseconds;
                        if (remaining <= 0) return null;

                        Task<UdpReceiveResult> receive = client.ReceiveAsync();
                        if (await Task.WhenAny(receive, Task.Delay(remaining)).ConfigureAwait(false) != receive)
                        {
                            // 超时后 UdpClient 被释放，未完成的接收会出错，这里观察掉以免成为未处理异常
                            _ = receive.ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                            return null;
                        }

                        UdpReceiveResult result = await receive.ConfigureAwait(false);
                        if (IsResponseTo(result.Buffer, id)) return watch.ElapsedMilliseconds;
                        // 其他报文（如迟到的旧应答）忽略，继续等待
                    }
                }
                catch (SocketException)
                {
                    // 如对方端口不可达（ICMP），或本机无可用网络
                    return null;
                }
                catch (ObjectDisposedException)
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// 对一个服务器依次查询多次，取成功结果的中位数
        /// </summary>
        public static async Task<DnsBenchmarkResult> BenchmarkAsync(IPAddress server, int attempts = 3, int timeoutMilliseconds = 1500)
        {
            var times = new List<long>();
            for (int i = 0; i < attempts; i++)
            {
                long? elapsed = await QueryAsync(server, ProbeNames[i % ProbeNames.Length], timeoutMilliseconds).ConfigureAwait(false);
                if (elapsed.HasValue) times.Add(elapsed.Value);
            }
            times.Sort();
            return new DnsBenchmarkResult
            {
                Server = server,
                Attempts = attempts,
                Succeeded = times.Count,
                MedianMilliseconds = times.Count == 0 ? (long?)null : times[times.Count / 2]
            };
        }
    }
}
