using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace IP_UpdateTest.Core
{
    /// <summary>
    /// 应用静态 IP 前检测该地址是否已被局域网内其他设备使用
    /// </summary>
    public static class IpConflict
    {
        [DllImport("iphlpapi.dll")]
        private static extern int SendARP(uint destIp, uint srcIp, byte[] macAddress, ref uint macLength);

        /// <summary>
        /// 只有目标地址与网卡当前地址在同一网段时 ARP 探测才有意义；
        /// 跨网段的地址可能由网关代答，会误报（纯函数，便于测试）
        /// </summary>
        public static bool ShouldProbe(string currentIp, string currentMask, string targetIp)
        {
            IPAddress current, mask, target;
            if (!IpValidator.TryParseIPv4(currentIp, out current)
                || !IpValidator.TryParseIPv4(currentMask, out mask)
                || !IpValidator.TryParseIPv4(targetIp, out target))
                return false;
            return !target.Equals(current) && IpValidator.IsSameSubnet(current, target, mask);
        }

        /// <summary>
        /// 用 ARP 询问谁在使用该地址，返回应答设备的 MAC；无人应答时返回 null。
        /// 无人应答时会等待约 1～3 秒，请在后台线程调用。
        /// </summary>
        public static PhysicalAddress Probe(IPAddress address)
        {
            var mac = new byte[6];
            uint length = (uint)mac.Length;
            int error = SendARP(BitConverter.ToUInt32(address.GetAddressBytes(), 0), 0, mac, ref length);
            return error == 0 && length == mac.Length ? new PhysicalAddress(mac) : null;
        }
    }
}
