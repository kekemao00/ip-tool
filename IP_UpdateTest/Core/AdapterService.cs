using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using Microsoft.Win32;

namespace IP_UpdateTest.Core
{
    /// <summary>
    /// 修改网卡配置的方式
    /// </summary>
    public enum BackendKind
    {
        /// <summary>
        /// 自动选择（只用于指定方式的参数）
        /// </summary>
        Auto,

        /// <summary>
        /// 不可修改
        /// </summary>
        None,

        Wmi,

        Netsh
    }

    /// <summary>
    /// 网卡查询与配置。
    /// 地址信息来自 NetworkInterface；状态、物理/虚拟、已禁用网卡来自 MSFT_NetAdapter（Windows 8+）。
    /// </summary>
    public static class AdapterService
    {
        private const string StandardCimv2 = @"root\StandardCimv2";

        /// <summary>
        /// MSFT_NetAdapter.State：已禁用
        /// </summary>
        private const uint StateDisabled = 3;

        /// <summary>
        /// MSFT_NetAdapter.MediaConnectState：已连接
        /// </summary>
        private const uint MediaConnected = 1;

        /// <summary>
        /// 判断“当前上网网卡”时用来查路由的公网地址（只查路由表，不发包）
        /// </summary>
        private static readonly IPAddress RouteProbeAddress = IPAddress.Parse("223.5.5.5");

        /// <summary>
        /// 各网卡保存的 TCP/IP 配置（netsh 和系统设置都写在这里，普通权限可读）
        /// </summary>
        private const string TcpipInterfacesKey = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\";

        /// <summary>
        /// 各网卡保存的 IPv6 配置（手动 DNS 在这里）
        /// </summary>
        private const string Tcpip6InterfacesKey = @"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters\Interfaces\";

        /// <summary>
        /// MSFT_NetIPAddress / MSFT_NetRoute.AddressFamily：IPv6
        /// </summary>
        private const int AddressFamilyIPv6 = 23;

        /// <summary>
        /// 读取网卡列表。包含 WMI 查询，耗时百毫秒级，请在后台线程调用。
        /// includeAll 为 false 时只返回物理网卡。
        /// </summary>
        public static List<NetworkAdapter> GetAdapters(bool includeAll)
        {
            var interfaces = new Dictionary<string, NetworkInterface>(StringComparer.OrdinalIgnoreCase);
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                interfaces[nic.Id] = nic;
            }

            Dictionary<string, WmiConfigState> configs = TryGetConfigurationStates();
            List<NetAdapterRecord> records = TryQueryNetAdapters();
            ManualIpv6Config ipv6 = TryQueryManualIpv6();
            var result = new List<NetworkAdapter>();

            if (records == null)
            {
                // MSFT_NetAdapter 需要 Windows 8 及以上；查询失败时按接口类型筛选
                foreach (NetworkInterface nic in interfaces.Values)
                {
                    bool ethernetOrWifi = nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet
                        || nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback || nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    if (!includeAll && !ethernetOrWifi) continue;

                    NetworkAdapter adapter = FromNetworkInterface(nic);
                    adapter.IsPhysical = ethernetOrWifi;
                    ReadSavedConfig(adapter);
                    ApplyConfigState(adapter, configs);
                    ApplyIpv6Config(adapter, ipv6);
                    result.Add(adapter);
                }
            }
            else
            {
                foreach (NetAdapterRecord record in records)
                {
                    if (record.Hidden) continue;
                    if (!includeAll && !record.HardwareInterface) continue;

                    // 已禁用的网卡不在 NetworkInterface 列表中，只有 MSFT_NetAdapter 的信息
                    NetworkInterface nic;
                    NetworkAdapter adapter = interfaces.TryGetValue(record.InterfaceGuid, out nic)
                        ? FromNetworkInterface(nic)
                        : new NetworkAdapter { NetworkInterfaceID = record.InterfaceGuid };

                    adapter.Name = record.Name;
                    adapter.InterfaceDescription = record.InterfaceDescription;
                    adapter.InterfaceIndex = (int)record.InterfaceIndex;
                    adapter.IsPhysical = record.HardwareInterface;
                    adapter.IsVirtual = record.Virtual;
                    adapter.Status = record.State == StateDisabled ? AdapterStatus.Disabled
                        : record.MediaConnectState == MediaConnected ? AdapterStatus.Connected
                        : AdapterStatus.Disconnected;
                    adapter.LinkSpeedBps = adapter.Status == AdapterStatus.Connected && record.Speed <= long.MaxValue ? (long)record.Speed : 0;
                    if (adapter.MacAddress == null) adapter.MacAddress = TryParseMac(record.PermanentAddress);

                    ReadSavedConfig(adapter);
                    ApplyConfigState(adapter, configs);
                    ApplyIpv6Config(adapter, ipv6);
                    result.Add(adapter);
                }
            }

            // 已连接的排前面，其次是物理网卡，再按名称
            return result
                .OrderBy(a => a.Status == AdapterStatus.Connected ? 0 : a.Status == AdapterStatus.Disconnected ? 1 : 2)
                .ThenBy(a => a.IsPhysical ? 0 : 1)
                .ThenBy(a => a.Name, StringComparer.CurrentCulture)
                .ToList();
        }

        /// <summary>
        /// 按连接名称、接口索引、GUID 或硬件描述查找网卡（命令行使用）
        /// </summary>
        public static NetworkAdapter MatchAdapter(IEnumerable<NetworkAdapter> adapters, string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            key = key.Trim();
            List<NetworkAdapter> list = adapters.ToList();

            int index;
            Guid guid;
            return list.FirstOrDefault(a => string.Equals(a.Name, key, StringComparison.CurrentCultureIgnoreCase))
                ?? (int.TryParse(key, out index) ? list.FirstOrDefault(a => a.InterfaceIndex == index) : null)
                ?? (Guid.TryParse(key, out guid) ? list.FirstOrDefault(a => Guid.TryParse(a.NetworkInterfaceID, out Guid id) && id == guid) : null)
                ?? list.FirstOrDefault(a => string.Equals(a.InterfaceDescription, key, StringComparison.CurrentCultureIgnoreCase));
        }

        /// <summary>
        /// 判定网卡的修改方式（纯函数，便于测试）
        /// </summary>
        public static BackendKind DetermineBackend(AdapterStatus status, bool wmiIpEnabled, bool hasInterfaceIndex,
            out string readOnlyReason, out string note)
        {
            readOnlyReason = null;
            note = null;

            if (status == AdapterStatus.Disabled)
            {
                readOnlyReason = "网卡已禁用，启用后才能修改配置";
                return BackendKind.None;
            }
            if (wmiIpEnabled) return BackendKind.Wmi;
            if (status == AdapterStatus.Connected)
            {
                readOnlyReason = "该网卡未启用 IPv4，或由系统、虚拟化软件（如 Hyper-V）管理，不支持修改";
                return BackendKind.None;
            }
            if (hasInterfaceIndex)
            {
                note = DisconnectedNote;
                return BackendKind.Netsh;
            }
            readOnlyReason = "无法识别该网卡的配置方式";
            return BackendKind.None;
        }

        /// <summary>
        /// 未连接网卡的说明。实测：网卡未连接时 netsh 和 MSFT_NetIPInterface 都无法关闭其 DHCP，
        /// 设置的静态地址会与 DHCP 并存，不能可靠生效，所以只开放 DHCP 和 DNS 设置。
        /// </summary>
        public const string DisconnectedNote = "网卡未连接：可改为自动获取或修改 DNS，静态 IP 需连接后设置";

        public const string StaticOnDisconnectedReason =
            "网卡未连接时 Windows 无法关闭它的 DHCP，设置的静态 IP 不能可靠生效。\n"
            + "请先插好网线或连接 Wi-Fi（对端没有 DHCP 服务也可以），再设置静态 IP。";

        /// <summary>
        /// 检查该网卡能否执行这次修改，不能时返回原因（纯函数，提权前即可检查）
        /// </summary>
        public static string CheckSupported(NetworkAdapter adapter, IpConfigRequest request, BackendKind forced = BackendKind.Auto)
        {
            BackendKind backend = forced != BackendKind.Auto ? forced : adapter.ConfigBackend;
            if (backend == BackendKind.None) return adapter.ReadOnlyReason ?? "该网卡不支持修改";
            if (backend == BackendKind.Netsh && forced == BackendKind.Auto)
            {
                if (!request.UseDhcp) return StaticOnDisconnectedReason;
                if (adapter.IsDhcpEnabled && !string.IsNullOrEmpty(adapter.ConfiguredIpAddress))
                    return "网卡未连接，且保存有与 DHCP 冲突的静态地址，请连接后再改为自动获取";
            }
            if (backend == BackendKind.Netsh && adapter.InterfaceIndex < 0) return "找不到该网卡的接口索引";
            return null;
        }

        /// <summary>
        /// 是否需要额外清除默认网关：WMI 无法清除网关，静态配置把网关留空且网卡原来有网关时需要（纯函数，便于测试）
        /// </summary>
        public static bool NeedsGatewayClear(string currentGateway, IpConfigRequest request)
        {
            return !request.UseDhcp && string.IsNullOrWhiteSpace(request.Gateway) && !string.IsNullOrEmpty(currentGateway);
        }

        /// <summary>
        /// 把配置应用到网卡。耗时操作（可达数秒），请在后台线程调用。
        /// forced 用于诊断时指定修改方式。
        /// </summary>
        public static ApplyResult Apply(NetworkAdapter adapter, IpConfigRequest request, BackendKind forced = BackendKind.Auto)
        {
            List<ValidationError> errors = request.Validate();
            if (errors.Count > 0) return ApplyResult.Fail(errors[0].Message);
            string unsupported = CheckSupported(adapter, request, forced);
            if (unsupported != null) return ApplyResult.Fail(unsupported);

            BackendKind backend = forced != BackendKind.Auto ? forced : adapter.ConfigBackend;
            string[] dns = request.UseDhcp && request.UseDhcpDns ? null : request.DnsServers;

            if (backend == BackendKind.Wmi)
            {
                if (request.UseDhcp) return WmiBackend.ApplyDhcp(adapter.NetworkInterfaceID, dns);

                ApplyResult result = WmiBackend.ApplyStatic(adapter.NetworkInterfaceID, request.IpAddress, request.SubnetMask, request.Gateway, dns);
                // 此时网卡已是静态地址，再由 netsh 清除原来的网关
                if (result.Success && NeedsGatewayClear(adapter.Gateway, request) && adapter.InterfaceIndex >= 0)
                    result = NetshBackend.ClearGateway(adapter.InterfaceIndex, request.IpAddress, request.SubnetMask);
                return result;
            }

            // netsh：未连接网卡只支持 DHCP 和 DNS；指定 --backend netsh 时也可设置静态地址（诊断用）
            if (!request.UseDhcp)
                return NetshBackend.ApplyStatic(adapter.InterfaceIndex, request.IpAddress, request.SubnetMask, request.Gateway, dns);
            return NetshBackend.ApplyDhcp(adapter.InterfaceIndex, dns, adapter.IsDhcpEnabled, adapter.IsDhcpEnabled && !adapter.HasStaticDns);
        }

        /// <summary>
        /// 启用或禁用网卡（MSFT_NetAdapter，替代已弃用的 Win32_NetworkAdapter）
        /// </summary>
        public static ApplyResult SetEnabled(NetworkAdapter adapter, bool enable)
        {
            string action = enable ? "启用" : "禁用";
            try
            {
                using (ManagementObject mo = FindNetAdapter(adapter.NetworkInterfaceID))
                {
                    if (mo == null) return ApplyResult.Fail($"{action}网卡失败：找不到该网卡");
                    using (ManagementBaseObject outParams = mo.InvokeMethod(enable ? "Enable" : "Disable", null, null))
                    {
                        uint code = Convert.ToUInt32(outParams["ReturnValue"]);
                        if (code == 0) return ApplyResult.Ok();
                        return ApplyResult.Fail(code == 5
                            ? $"{action}网卡失败：拒绝访问，需要管理员权限（错误码 5）"
                            : $"{action}网卡失败（错误码 {code}）");
                    }
                }
            }
            catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.AccessDenied)
            {
                return ApplyResult.Fail($"{action}网卡失败：拒绝访问，需要管理员权限");
            }
            catch (UnauthorizedAccessException)
            {
                return ApplyResult.Fail($"{action}网卡失败：拒绝访问，需要管理员权限");
            }
            catch (ManagementException ex)
            {
                return ApplyResult.Fail($"{action}网卡失败：{ex.Message.Trim()}");
            }
        }

        /// <summary>
        /// 续订 DHCP 租约（重新获取 IP），只适用于已连接且启用 DHCP 的网卡
        /// </summary>
        public static ApplyResult RenewDhcp(NetworkAdapter adapter)
        {
            if (!adapter.IsDhcpEnabled) return ApplyResult.Fail("该网卡未启用 DHCP");
            return WmiBackend.RenewDhcpLease(adapter.NetworkInterfaceID);
        }

        /// <summary>
        /// 检查该网卡能否修改 IPv6，不能时返回原因（纯函数，提权前即可检查）
        /// </summary>
        public static string CheckIpv6Supported(NetworkAdapter adapter)
        {
            if (!adapter.CanConfigure) return adapter.ReadOnlyReason ?? "该网卡不支持修改";
            if (!adapter.Ipv6Enabled) return "该网卡未启用 IPv6：请在网卡属性中勾选“Internet 协议版本 6 (TCP/IPv6)”";
            if (!adapter.Ipv6ConfigKnown) return "读取不到该网卡的 IPv6 配置（需要 Windows 8 及以上），无法修改";
            if (adapter.InterfaceIndex < 0) return "找不到该网卡的接口索引";
            return null;
        }

        /// <summary>
        /// 把 IPv6 配置应用到网卡。耗时操作，请在后台线程调用
        /// </summary>
        public static ApplyResult ApplyIpv6(NetworkAdapter adapter, Ipv6ConfigRequest request)
        {
            List<ValidationError> errors = request.Validate();
            if (errors.Count > 0) return ApplyResult.Fail(errors[0].Message);
            string unsupported = CheckIpv6Supported(adapter);
            if (unsupported != null) return ApplyResult.Fail(unsupported);
            ApplyResult result = Ipv6Backend.Apply(adapter.InterfaceIndex, adapter.Ipv6ManualAddresses, adapter.Ipv6ManualGateways, adapter.HasStaticIpv6Dns, request);

            // 与 IPv4 的 ARP 冲突检查对应：IPv6 由系统做重复地址检测，地址冲突时系统不会使用它
            IPAddress address;
            if (!result.Success || request.UseAuto || adapter.Status != AdapterStatus.Connected || !Ipv6Validator.TryParse(request.Address, out address))
                return result;
            DuplicateAddressDetectionState? state = Ipv6Backend.WaitForDad(() => ReadDadState(adapter.NetworkInterfaceID, address), 12, () => Thread.Sleep(250));
            if (state != DuplicateAddressDetectionState.Duplicate) return result;

            // 这次新加的地址撤销掉，免得留下一个用不了的手动地址；原来就有的不动
            bool added = !adapter.Ipv6ManualAddresses.Any(m => Ipv6Validator.Normalize(m.Address) == address.ToString());
            if (added) NetshBackend.Run(new[] { Ipv6Backend.DeleteAddressCommand(adapter.InterfaceIndex, address) });
            return ApplyResult.Fail($"IPv6 地址 {address} 已被网段内的其他设备使用（重复地址检测失败），系统不会使用它"
                + (added ? "，已撤销这个地址。" : "。") + "请换一个地址再应用");
        }

        /// <summary>
        /// 读取网卡上某个 IPv6 地址的重复地址检测状态，找不到时返回 null
        /// </summary>
        private static DuplicateAddressDetectionState? ReadDadState(string interfaceId, IPAddress address)
        {
            try
            {
                NetworkInterface nic = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(n => string.Equals(n.Id, interfaceId, StringComparison.OrdinalIgnoreCase));
                UnicastIPAddressInformation info = nic?.GetIPProperties().UnicastAddresses
                    .FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetworkV6 && new IPAddress(u.Address.GetAddressBytes()).Equals(address));
                return info?.DuplicateAddressDetectionState;
            }
            catch (NetworkInformationException)
            {
                return null;
            }
        }

        /// <summary>
        /// 当前上网网卡（默认路由所在网卡）的接口索引，无法确定时返回 -1
        /// </summary>
        public static int GetPrimaryInterfaceIndex()
        {
            uint index;
            uint destination = BitConverter.ToUInt32(RouteProbeAddress.GetAddressBytes(), 0);
            return GetBestInterface(destination, out index) == 0 ? (int)index : -1;
        }

        /// <summary>
        /// 由 NetworkInterface 生成网卡信息（地址、DHCP 状态等）
        /// </summary>
        private static NetworkAdapter FromNetworkInterface(NetworkInterface nic)
        {
            IPInterfaceProperties ips = nic.GetIPProperties();
            IPv4InterfaceProperties ipv4 = null;
            try
            {
                ipv4 = ips.GetIPv4Properties();
            }
            catch (NetworkInformationException)
            {
                // 网卡未启用 IPv4
            }

            bool isUp = nic.OperationalStatus == OperationalStatus.Up;
            return new NetworkAdapter
            {
                NetworkInterfaceID = nic.Id,
                Name = nic.Name,
                InterfaceDescription = nic.Description,
                InterfaceIndex = ipv4 == null ? -1 : ipv4.Index,
                NetworkInterfaceType = nic.NetworkInterfaceType.ToString(),
                Status = isUp ? AdapterStatus.Connected : AdapterStatus.Disconnected,
                LinkSpeedBps = isUp ? nic.Speed : 0,
                MacAddress = nic.GetPhysicalAddress(),
                Gateways = ips.GatewayAddresses,
                IPAddresses = ips.UnicastAddresses,
                DhcpServerAddresses = ips.DhcpServerAddresses,
                DnsAddresses = ips.DnsAddresses,
                IsDhcpEnabled = ipv4 != null && ipv4.IsDhcpEnabled,
                Ipv6Enabled = nic.Supports(NetworkInterfaceComponent.IPv6),
                Ipv6LiveAddresses = Ipv6Status.FromSystem(ips.UnicastAddresses),
                Ipv6LiveGateways = ips.GatewayAddresses.Select(g => g.Address).Where(a => a.AddressFamily == AddressFamily.InterNetworkV6
                    && !a.Equals(IPAddress.IPv6Any)).Select(ScopeFree).Distinct().ToList(),
                // 没有任何 IPv6 DNS 时 Windows 报告 fec0:0:0:ffff::1 等已废弃的默认地址，不算
                Ipv6LiveDns = ips.DnsAddresses.Where(a => a.AddressFamily == AddressFamily.InterNetworkV6 && !a.IsIPv6SiteLocal)
                    .Select(ScopeFree).Distinct().ToList()
            };
        }

        /// <summary>
        /// 去掉作用域后缀，如 fe80::1%12 → fe80::1
        /// </summary>
        private static string ScopeFree(IPAddress address)
        {
            return new IPAddress(address.GetAddressBytes()).ToString();
        }

        /// <summary>
        /// 读取网卡保存的 IPv4 配置：静态 IP、手动 DNS、是否启用 DHCP
        /// </summary>
        private static void ReadSavedConfig(NetworkAdapter adapter)
        {
            if (string.IsNullOrEmpty(adapter.NetworkInterfaceID)) return;
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(TcpipInterfacesKey + adapter.NetworkInterfaceID))
                {
                    if (key == null) return;

                    adapter.ConfiguredIpAddress = FirstAddress(key.GetValue("IPAddress"));
                    adapter.ConfiguredSubnetMask = FirstAddress(key.GetValue("SubnetMask"));
                    adapter.ConfiguredGateway = FirstAddress(key.GetValue("DefaultGateway"));
                    adapter.ConfiguredDns = SplitServers(key.GetValue("NameServer") as string);

                    // 未连接或已禁用时系统不报告地址信息，DHCP 状态以保存的配置为准
                    if (adapter.Status != AdapterStatus.Connected && key.GetValue("EnableDHCP") is int enableDhcp)
                        adapter.IsDhcpEnabled = enableDhcp != 0;
                }
            }
            catch (Exception ex) when (ex is SecurityException || ex is IOException || ex is UnauthorizedAccessException)
            {
                // 读不到保存的配置时只显示系统报告的地址
            }

            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(Tcpip6InterfacesKey + adapter.NetworkInterfaceID))
                {
                    if (key != null) adapter.ConfiguredIpv6Dns = SplitServers(key.GetValue("NameServer") as string);
                }
            }
            catch (Exception ex) when (ex is SecurityException || ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        /// <summary>
        /// 注册表中的地址可能是 REG_MULTI_SZ 或 REG_SZ，取第一个有效值（DHCP 网卡常为 0.0.0.0）
        /// </summary>
        private static string FirstAddress(object value)
        {
            IEnumerable<string> values = value as string[] ?? new[] { value as string };
            return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v) && v.Trim() != "0.0.0.0")?.Trim();
        }

        /// <summary>
        /// NameServer 中的多个 DNS 以逗号或空格分隔
        /// </summary>
        public static string[] SplitServers(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return new string[0];
            return value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
        }

        private static void ApplyConfigState(NetworkAdapter adapter, Dictionary<string, WmiConfigState> configs)
        {
            WmiConfigState state = null;
            if (configs != null && adapter.NetworkInterfaceID != null) configs.TryGetValue(adapter.NetworkInterfaceID, out state);

            string readOnlyReason;
            string note;
            adapter.ConfigBackend = DetermineBackend(adapter.Status, state != null && state.IpEnabled, adapter.InterfaceIndex >= 0,
                out readOnlyReason, out note);
            adapter.ReadOnlyReason = readOnlyReason;
            adapter.ConfigNote = note;

            if (state != null && adapter.IsDhcpEnabled && adapter.Status == AdapterStatus.Connected)
            {
                adapter.DhcpLeaseObtained = state.LeaseObtained;
                adapter.DhcpLeaseExpires = state.LeaseExpires;
            }
        }

        private static void ApplyIpv6Config(NetworkAdapter adapter, ManualIpv6Config ipv6)
        {
            if (ipv6 == null) return;
            adapter.Ipv6ConfigKnown = true;
            List<Ipv6ManualAddress> addresses;
            if (ipv6.Addresses.TryGetValue(adapter.InterfaceIndex, out addresses)) adapter.Ipv6ManualAddresses = addresses;
            List<string> gateways;
            if (ipv6.Gateways.TryGetValue(adapter.InterfaceIndex, out gateways)) adapter.Ipv6ManualGateways = gateways;
        }

        /// <summary>
        /// 所有网卡手动设置的 IPv6 地址和默认网关，按接口索引分组；查询失败（Windows 7 等）时返回 null。
        /// 持久存储里的是管理员设置的，路由器通告得到的只在活动存储中，且协议不是 NetMgmt
        /// </summary>
        private static ManualIpv6Config TryQueryManualIpv6()
        {
            try
            {
                var config = new ManualIpv6Config();
                var scope = new ManagementScope(StandardCimv2);
                // PrefixOrigin 1：手动
                var addressQuery = new ObjectQuery("SELECT InterfaceIndex, IPAddress, PrefixLength FROM MSFT_NetIPAddress WHERE AddressFamily = "
                    + AddressFamilyIPv6 + " AND PrefixOrigin = 1");
                using (var searcher = new ManagementObjectSearcher(scope, addressQuery))
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementObject mo in results)
                    {
                        using (mo)
                        {
                            string address = Ipv6Validator.Normalize(mo["IPAddress"] as string);
                            List<Ipv6ManualAddress> list = config.AddressesOf(Convert.ToInt32(mo["InterfaceIndex"] ?? 0u));
                            // 活动存储和持久存储各有一份
                            if (address.Length > 0 && !list.Any(a => a.Address == address))
                                list.Add(new Ipv6ManualAddress(address, Convert.ToInt32(mo["PrefixLength"] ?? (byte)0)));
                        }
                    }
                }

                var routeQuery = new ObjectQuery("SELECT InterfaceIndex, NextHop, Store, Protocol FROM MSFT_NetRoute WHERE AddressFamily = "
                    + AddressFamilyIPv6 + " AND DestinationPrefix = '::/0'");
                using (var searcher = new ManagementObjectSearcher(scope, routeQuery))
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementObject mo in results)
                    {
                        using (mo)
                        {
                            // Store 0：持久存储；Protocol 3：NetMgmt（手动设置）
                            bool manual = Convert.ToInt32(mo["Store"] ?? (byte)1) == 0 || Convert.ToInt32(mo["Protocol"] ?? (ushort)0) == 3;
                            string nextHop = Ipv6Validator.Normalize(mo["NextHop"] as string);
                            if (!manual || nextHop.Length == 0 || nextHop == "::") continue;
                            List<string> list = config.GatewaysOf(Convert.ToInt32(mo["InterfaceIndex"] ?? 0u));
                            if (!list.Contains(nextHop)) list.Add(nextHop);
                        }
                    }
                }
                return config;
            }
            catch (Exception ex) when (ex is ManagementException || ex is COMException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static Dictionary<string, WmiConfigState> TryGetConfigurationStates()
        {
            try
            {
                return WmiBackend.GetConfigurationStates();
            }
            catch (Exception ex) when (ex is ManagementException || ex is COMException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static List<NetAdapterRecord> TryQueryNetAdapters()
        {
            try
            {
                var records = new List<NetAdapterRecord>();
                var query = new ObjectQuery("SELECT InterfaceGuid, Name, InterfaceDescription, InterfaceIndex, HardwareInterface, "
                    + "Virtual, Hidden, State, MediaConnectState, Speed, PermanentAddress FROM MSFT_NetAdapter");
                using (var searcher = new ManagementObjectSearcher(new ManagementScope(StandardCimv2), query))
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementObject mo in results)
                    {
                        using (mo)
                        {
                            records.Add(new NetAdapterRecord
                            {
                                InterfaceGuid = mo["InterfaceGuid"] as string ?? "",
                                Name = mo["Name"] as string ?? "",
                                InterfaceDescription = mo["InterfaceDescription"] as string ?? "",
                                InterfaceIndex = Convert.ToUInt32(mo["InterfaceIndex"] ?? 0u),
                                HardwareInterface = mo["HardwareInterface"] as bool? == true,
                                Virtual = mo["Virtual"] as bool? == true,
                                Hidden = mo["Hidden"] as bool? == true,
                                State = Convert.ToUInt32(mo["State"] ?? 0u),
                                MediaConnectState = Convert.ToUInt32(mo["MediaConnectState"] ?? 0u),
                                Speed = Convert.ToUInt64(mo["Speed"] ?? 0ul),
                                PermanentAddress = mo["PermanentAddress"] as string
                            });
                        }
                    }
                }
                return records;
            }
            catch (Exception ex) when (ex is ManagementException || ex is COMException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static ManagementObject FindNetAdapter(string interfaceGuid)
        {
            Guid guid;
            if (!Guid.TryParse(interfaceGuid, out guid)) return null;

            var query = new ObjectQuery("SELECT * FROM MSFT_NetAdapter WHERE InterfaceGuid = '"
                + guid.ToString("B").ToUpperInvariant() + "'");
            using (var searcher = new ManagementObjectSearcher(new ManagementScope(StandardCimv2), query))
            using (ManagementObjectCollection results = searcher.Get())
            {
                foreach (ManagementObject mo in results)
                {
                    return mo;
                }
            }
            return null;
        }

        private static PhysicalAddress TryParseMac(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            try
            {
                return PhysicalAddress.Parse(text.Trim().Replace(":", "-").ToUpperInvariant());
            }
            catch (FormatException)
            {
                return null;
            }
        }

        [DllImport("iphlpapi.dll")]
        private static extern int GetBestInterface(uint destAddr, out uint bestIfIndex);

        private sealed class ManualIpv6Config
        {
            public readonly Dictionary<int, List<Ipv6ManualAddress>> Addresses = new Dictionary<int, List<Ipv6ManualAddress>>();
            public readonly Dictionary<int, List<string>> Gateways = new Dictionary<int, List<string>>();

            public List<Ipv6ManualAddress> AddressesOf(int interfaceIndex)
            {
                List<Ipv6ManualAddress> list;
                if (!Addresses.TryGetValue(interfaceIndex, out list)) Addresses[interfaceIndex] = list = new List<Ipv6ManualAddress>();
                return list;
            }

            public List<string> GatewaysOf(int interfaceIndex)
            {
                List<string> list;
                if (!Gateways.TryGetValue(interfaceIndex, out list)) Gateways[interfaceIndex] = list = new List<string>();
                return list;
            }
        }

        private sealed class NetAdapterRecord
        {
            public string InterfaceGuid;
            public string Name;
            public string InterfaceDescription;
            public uint InterfaceIndex;
            public bool HardwareInterface;
            public bool Virtual;
            public bool Hidden;
            public uint State;
            public uint MediaConnectState;
            public ulong Speed;
            public string PermanentAddress;
        }
    }
}
