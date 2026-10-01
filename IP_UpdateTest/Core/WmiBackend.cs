using System;
using System.Collections.Generic;
using System.Management;

namespace IP_UpdateTest.Core
{
    /// <summary>
    /// Win32_NetworkAdapterConfiguration 中的网卡状态
    /// </summary>
    public sealed class WmiConfigState
    {
        public bool IpEnabled { get; set; }
        public bool DhcpEnabled { get; set; }
        public DateTime? LeaseObtained { get; set; }
        public DateTime? LeaseExpires { get; set; }
    }

    /// <summary>
    /// 通过 WMI Win32_NetworkAdapterConfiguration 修改 IPv4 配置。
    /// 只能配置已连接（IPEnabled=True）的网卡。
    /// </summary>
    public static class WmiBackend
    {
        /// <summary>
        /// 按 SettingID（即 NetworkInterface.Id）查询网卡配置实例，找不到返回 null
        /// </summary>
        public static ManagementObject FindConfiguration(string settingId)
        {
            Guid guid;
            if (!Guid.TryParse(settingId, out guid)) return null;

            string query = "SELECT * FROM Win32_NetworkAdapterConfiguration WHERE SettingID = '"
                + guid.ToString("B").ToUpperInvariant() + "'";
            using (var searcher = new ManagementObjectSearcher(query))
            using (var results = searcher.Get())
            {
                foreach (ManagementObject mo in results)
                {
                    return mo;
                }
            }
            return null;
        }

        /// <summary>
        /// 一次查询所有网卡的配置状态，键为 SettingID
        /// </summary>
        public static Dictionary<string, WmiConfigState> GetConfigurationStates()
        {
            var states = new Dictionary<string, WmiConfigState>(StringComparer.OrdinalIgnoreCase);
            string query = "SELECT SettingID, IPEnabled, DHCPEnabled, DHCPLeaseObtained, DHCPLeaseExpires FROM Win32_NetworkAdapterConfiguration";
            using (var searcher = new ManagementObjectSearcher(query))
            using (var results = searcher.Get())
            {
                foreach (ManagementObject mo in results)
                {
                    using (mo)
                    {
                        string id = mo["SettingID"] as string;
                        if (string.IsNullOrEmpty(id)) continue;
                        states[id] = new WmiConfigState
                        {
                            IpEnabled = IsIpEnabled(mo),
                            DhcpEnabled = mo["DHCPEnabled"] as bool? == true,
                            LeaseObtained = ToDateTime(mo["DHCPLeaseObtained"] as string),
                            LeaseExpires = ToDateTime(mo["DHCPLeaseExpires"] as string)
                        };
                    }
                }
            }
            return states;
        }

        /// <summary>
        /// WMI 的 DMTF 时间字符串转本地时间
        /// </summary>
        private static DateTime? ToDateTime(string dmtf)
        {
            if (string.IsNullOrEmpty(dmtf)) return null;
            try
            {
                return ManagementDateTimeConverter.ToDateTime(dmtf);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        /// <summary>
        /// 设置静态 IP。gateway 为空时保留原网关（WMI 无法清除网关）；
        /// dns 为 null 时保留原 DNS，为空数组时清空 DNS
        /// </summary>
        public static ApplyResult ApplyStatic(string settingId, string ipAddress, string subnetMask, string gateway, string[] dns)
        {
            return Run(settingId, mo =>
            {
                uint code = Invoke(mo, "EnableStatic", p =>
                {
                    p["IPAddress"] = new[] { ipAddress };
                    p["SubnetMask"] = new[] { subnetMask };
                });
                if (!WmiReturnCode.IsSuccess("EnableStatic", code))
                    return ApplyResult.Fail("设置 IP 地址和子网掩码失败：" + WmiReturnCode.Describe(code));

                if (!string.IsNullOrEmpty(gateway))
                {
                    code = Invoke(mo, "SetGateways", p => p["DefaultIPGateway"] = new[] { gateway });
                    if (!WmiReturnCode.IsSuccess("SetGateways", code))
                        return ApplyResult.Fail("设置网关失败：" + WmiReturnCode.Describe(code));
                }

                if (dns != null)
                {
                    // 空列表传 null：静态地址下即清空 DNS
                    string[] servers = dns.Length > 0 ? dns : null;
                    code = Invoke(mo, "SetDNSServerSearchOrder", p => p["DNSServerSearchOrder"] = servers);
                    if (!WmiReturnCode.IsSuccess("SetDNSServerSearchOrder", code))
                        return ApplyResult.Fail("设置 DNS 失败：" + WmiReturnCode.Describe(code));
                }

                return ApplyResult.Ok();
            });
        }

        /// <summary>
        /// 续订 DHCP 租约（重新获取 IP）
        /// </summary>
        public static ApplyResult RenewDhcpLease(string settingId)
        {
            return Run(settingId, mo =>
            {
                uint code = Invoke(mo, "RenewDHCPLease", null);
                return WmiReturnCode.IsSuccess("RenewDHCPLease", code)
                    ? ApplyResult.Ok()
                    : ApplyResult.Fail("重新获取 IP 失败：" + WmiReturnCode.Describe(code));
            });
        }

        /// <summary>
        /// 启用 DHCP；dns 为空时 DNS 也改为自动获取，否则使用指定 DNS
        /// </summary>
        public static ApplyResult ApplyDhcp(string settingId, string[] dns)
        {
            return Run(settingId, mo =>
            {
                uint code = Invoke(mo, "EnableDHCP", null);
                if (!WmiReturnCode.IsSuccess("EnableDHCP", code) && !IsDhcpEnabled(settingId))
                    return ApplyResult.Fail("启用 DHCP 失败：" + WmiReturnCode.Describe(code));

                // DNSServerSearchOrder 为 null 表示 DNS 改为自动获取
                string[] servers = dns != null && dns.Length > 0 ? dns : null;
                code = Invoke(mo, "SetDNSServerSearchOrder", p => p["DNSServerSearchOrder"] = servers);
                if (!WmiReturnCode.IsSuccess("SetDNSServerSearchOrder", code))
                    return ApplyResult.Fail("设置 DNS 失败：" + WmiReturnCode.Describe(code));

                return ApplyResult.Ok();
            });
        }

        private static ApplyResult Run(string settingId, Func<ManagementObject, ApplyResult> action)
        {
            try
            {
                using (var mo = FindConfiguration(settingId))
                {
                    if (mo == null)
                        return ApplyResult.Fail("该网卡不支持通过 WMI 配置（可能由系统或虚拟化软件管理）");
                    if (!IsIpEnabled(mo))
                        return ApplyResult.Fail(WmiReturnCode.Describe(WmiReturnCode.IpNotEnabled));
                    return action(mo);
                }
            }
            catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.AccessDenied)
            {
                return ApplyResult.Fail(WmiReturnCode.Describe(WmiReturnCode.AccessDenied));
            }
            catch (UnauthorizedAccessException)
            {
                return ApplyResult.Fail(WmiReturnCode.Describe(WmiReturnCode.AccessDenied));
            }
            catch (ManagementException ex)
            {
                return ApplyResult.Fail("WMI 调用失败：" + ex.Message);
            }
        }

        private static uint Invoke(ManagementObject mo, string method, Action<ManagementBaseObject> setParameters)
        {
            ManagementBaseObject inParams = null;
            if (setParameters != null)
            {
                inParams = mo.GetMethodParameters(method);
                setParameters(inParams);
            }
            using (var outParams = mo.InvokeMethod(method, inParams, null))
            {
                return Convert.ToUInt32(outParams["ReturnValue"]);
            }
        }

        private static bool IsIpEnabled(ManagementObject mo)
        {
            return mo["IPEnabled"] as bool? == true;
        }

        private static bool IsDhcpEnabled(string settingId)
        {
            using (var mo = FindConfiguration(settingId))
            {
                return mo != null && mo["DHCPEnabled"] as bool? == true;
            }
        }
    }
}
