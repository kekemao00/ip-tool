using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace IP_UpdateTest.Core.Diagnosis
{
    /// <summary>
    /// STUN（RFC 5389）绑定请求：检测 UDP 能否到达公网，并取得 NAT 映射后的公网地址和端口。
    /// 同一个本地端口先后询问两台服务器，映射端口不同说明是对称型 NAT。
    /// </summary>
    public sealed class StunProbe : IDisposable
    {
        public const uint MagicCookie = 0x2112A442;

        /// <summary>
        /// 国内可用的公共 STUN 服务器，最后一个作为备用
        /// </summary>
        public static readonly string[] Servers = { "stun.miwifi.com", "stun.chat.bilibili.com", "stun.cloudflare.com" };

        public const int Port = 3478;

        private static readonly Random IdSource = new Random();

        private readonly UdpClient client;
        private Task<UdpReceiveResult> pending;

        public StunProbe()
        {
            client = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
            try
            {
                // 对方端口不可达时 Windows 会让后续接收报错（WSAECONNRESET），关闭该行为
                const int SioUdpConnReset = -1744830452;
                client.Client.IOControl(SioUdpConnReset, new byte[] { 0 }, null);
            }
            catch (SocketException)
            {
            }
            catch (PlatformNotSupportedException)
            {
            }
        }

        /// <summary>
        /// 构造绑定请求：类型 0x0001、长度 0、Magic Cookie、12 字节事务 ID
        /// </summary>
        public static byte[] BuildBindingRequest(byte[] transactionId)
        {
            if (transactionId == null || transactionId.Length != 12) throw new ArgumentException("事务 ID 必须是 12 字节", nameof(transactionId));
            var packet = new byte[20];
            packet[1] = 0x01;
            packet[4] = 0x21;
            packet[5] = 0x12;
            packet[6] = 0xA4;
            packet[7] = 0x42;
            Buffer.BlockCopy(transactionId, 0, packet, 8, 12);
            return packet;
        }

        /// <summary>
        /// 解析绑定成功响应中的映射地址（优先 XOR-MAPPED-ADDRESS，其次 MAPPED-ADDRESS），只支持 IPv4；不是对应的响应时返回 null
        /// </summary>
        public static IPEndPoint ParseBindingResponse(byte[] data, byte[] transactionId)
        {
            if (data == null || data.Length < 20 || data[0] != 0x01 || data[1] != 0x01) return null;
            if (data[4] != 0x21 || data[5] != 0x12 || data[6] != 0xA4 || data[7] != 0x42) return null;
            for (int i = 0; i < 12; i++)
            {
                if (data[8 + i] != transactionId[i]) return null;
            }

            int length = (data[2] << 8) | data[3];
            int end = Math.Min(data.Length, 20 + length);
            IPEndPoint mapped = null;
            int offset = 20;
            while (offset + 4 <= end)
            {
                int type = (data[offset] << 8) | data[offset + 1];
                int attrLength = (data[offset + 2] << 8) | data[offset + 3];
                int value = offset + 4;
                if (value + attrLength > end) break;

                // 地址属性：1 字节保留、1 字节地址族（0x01 = IPv4）、2 字节端口、4 字节地址
                if (attrLength >= 8 && data[value + 1] == 0x01 && (type == 0x0020 || type == 0x0001))
                {
                    int port = (data[value + 2] << 8) | data[value + 3];
                    var address = new byte[4];
                    Buffer.BlockCopy(data, value + 4, address, 0, 4);
                    if (type == 0x0020)
                    {
                        port ^= (int)(MagicCookie >> 16);
                        for (int i = 0; i < 4; i++) address[i] ^= (byte)(MagicCookie >> (24 - 8 * i));
                        return new IPEndPoint(new IPAddress(address), port);
                    }
                    if (mapped == null) mapped = new IPEndPoint(new IPAddress(address), port);
                }
                offset = value + ((attrLength + 3) & ~3);
            }
            return mapped;
        }

        /// <summary>
        /// 向一台服务器发送绑定请求，最多尝试 attempts 次；无应答返回 null
        /// </summary>
        public async Task<IPEndPoint> QueryAsync(IPEndPoint server, int timeoutMilliseconds = 1500, int attempts = 2)
        {
            var id = new byte[12];
            lock (IdSource)
            {
                IdSource.NextBytes(id);
            }
            byte[] request = BuildBindingRequest(id);

            for (int attempt = 0; attempt < attempts; attempt++)
            {
                try
                {
                    await client.SendAsync(request, request.Length, server).ConfigureAwait(false);
                    Stopwatch watch = Stopwatch.StartNew();
                    while (true)
                    {
                        int remaining = timeoutMilliseconds - (int)watch.ElapsedMilliseconds;
                        if (remaining <= 0) break;

                        // 上次超时未完成的接收继续沿用，避免两个接收同时等待抢走应答
                        if (pending == null) pending = client.ReceiveAsync();
                        if (await Task.WhenAny(pending, Task.Delay(remaining)).ConfigureAwait(false) != pending) break;

                        Task<UdpReceiveResult> done = pending;
                        pending = null;
                        IPEndPoint mapped = ParseBindingResponse((await done.ConfigureAwait(false)).Buffer, id);
                        if (mapped != null) return mapped;
                    }
                }
                catch (SocketException)
                {
                    pending = null;
                }
                catch (ObjectDisposedException)
                {
                    return null;
                }
            }
            return null;
        }

        public void Dispose()
        {
            // 释放后未完成的接收会出错，这里观察掉以免成为未处理异常
            if (pending != null) _ = pending.ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            client.Dispose();
        }
    }
}
