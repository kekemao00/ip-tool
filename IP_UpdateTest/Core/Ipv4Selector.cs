using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace IP_UpdateTest.Core
{
    /// <summary>
    /// 从网卡地址集合中挑出 IPv4 项。
    /// 双栈环境下集合里混有 IPv6（链路本地、临时地址等），且顺序不固定，不能按下标取。
    /// </summary>
    public static class Ipv4Selector
    {
        /// <summary>
        /// 是否为可用的 IPv4 地址（排除 0.0.0.0）
        /// </summary>
        public static bool IsUsableIPv4(IPAddress address)
        {
            return address != null
                && address.AddressFamily == AddressFamily.InterNetwork
                && !IPAddress.Any.Equals(address);
        }

        /// <summary>
        /// 取集合中第一个 IPv4 项，没有则返回 null
        /// </summary>
        public static T FirstIPv4<T>(IEnumerable<T> items, Func<T, IPAddress> getAddress) where T : class
        {
            if (items == null) return null;
            return items.FirstOrDefault(item => IsUsableIPv4(getAddress(item)));
        }

        /// <summary>
        /// 只保留 IPv4 地址，保持原有顺序
        /// </summary>
        public static List<IPAddress> IPv4Only(IEnumerable<IPAddress> addresses)
        {
            if (addresses == null) return new List<IPAddress>();
            return addresses.Where(IsUsableIPv4).ToList();
        }
    }
}
