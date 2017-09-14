using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;

namespace IP_UpdateTest
{
    /// <summary>
    /// 网络适配器类
    /// </summary>
    public class NetworkAdapter
    {

        #region 属性
        /// <summary>
        /// 网络适配器标识符，如：{274F9DD5-3650-4D59-B61E-710B6AF5AB36}
        /// </summary>
        public string NetworkInterfaceID { get; set; }

        /// <summary>
        /// IP地址
        /// </summary>
        public string IpAddress
        {
            get
            {
                string ipAddress = "";
                UnicastIPAddressInformation address = GetAddress();
                if (address == null)
                {
                    ipAddress = "";
                }
                else
                {
                    ipAddress = address.Address.ToString();
                }
                return ipAddress;
            }
            set { }
        }

        /// <summary>
        /// 网关地址
        /// </summary>
        public string Getway
        {
            get
            {

                string getway = "";
                if (Getwaryes != null && Getwaryes.Count > 0)
                {

                    getway = Getwaryes[0].Address.ToString() == "0.0.0.0" ? "" : Getwaryes[0].Address.ToString(); //发现用.netframework4.0 不会出现0.0.0.0
                }
                else
                {
                    getway = "";
                }
                return getway;
            }
            set { }
        }

        /// <summary>
        /// DHCP服务器地址
        /// </summary>
        public string DhcpServer
        {
            get
            {
                string dhcpServer = "";
                if (DhcpServerAddresses != null && DhcpServerAddresses.Count > 0)
                {
                    dhcpServer = DhcpServerAddresses[0].ToString();
                }
                else
                {
                    dhcpServer = "";
                }
                return dhcpServer;
            }
            set { }
        }

        /// <summary>
        /// MAC地址
        /// </summary>
        public string MacAddres
        {
            get
            {
                string macAddress = "";
                if (MacAddress == null)
                    macAddress = "";
                else if (MacAddress.ToString().Length == 12)
                {
                    macAddress = MacAddress.ToString().Insert(4, "-").Insert(9, "-");
                }
                else
                {
                    macAddress = MacAddress.ToString();
                }
                return macAddress;
            }
            set { }
        }

        /// <summary>
        /// 主DNS地址
        /// </summary>
        public string DnsMain
        {
            get
            {
                string dnsMain = "";

                if (DnsAddresses.Count > 0)
                {
                    if (IsIPAddress(DnsAddresses[0].ToString()))
                    {
                        dnsMain = DnsAddresses[0].ToString();
                    }
                }
                else
                {
                    dnsMain = "";
                }
                return dnsMain;
            }
            set { }
        }

        /// <summary>
        /// 备用DNS地址
        /// </summary>
        public string DnsBackup
        {

            get
            {
                string dnsBackup = "";
                if (DnsAddresses.Count > 1)
                {
                    if (IsIPAddress(DnsAddresses[1].ToString()))
                    {
                        dnsBackup = DnsAddresses[1].ToString();
                    }

                }
                else
                {
                    dnsBackup = "";
                }
                return dnsBackup;
            }
            set { }
        }

        /// <summary>
        /// 子网掩码
        /// </summary>
        public string Mask
        {
            get
            {
                string mask = "";
                UnicastIPAddressInformation address = GetAddress();
                if (address == null)
                {
                    mask = "";
                }
                else
                {
                    if (address.IPv4Mask != null) //用.netframwork4.0测试 address.IPv4Mask不存在null, 而为空是255.255.255.0
                    {
                        mask = address.IPv4Mask.ToString();
                    }
                    else
                    {
                        mask = "255.255.255.0";
                    }

                }
                return mask;
            }
            set { }

        }

        /// <summary>
        /// DNS集合
        /// </summary>
        public IPAddressCollection DnsAddresses { get; set; }
        /// <summary>
        /// 网关地址集合
        /// </summary>
        public GatewayIPAddressInformationCollection Getwaryes { get; set; }

        /// <summary>
        /// IP地址集合
        /// </summary>
        public UnicastIPAddressInformationCollection IPAddresses { get; set; }

        /// <summary>
        /// DHCP地址集合
        /// </summary>
        public IPAddressCollection DhcpServerAddresses { get; set; }

        /// <summary>
        /// 网卡MAC地址
        /// </summary>
        public PhysicalAddress MacAddress { get; set; }

        /// <summary>
        /// 是否启用DHCP服务
        /// </summary>
        public bool IsDhcpEnabled { get; set; }

        /// <summary>
        /// 描述信息
        /// </summary>
        public string Description { get; set; }
        /// <summary>
        /// 网络接口类型
        /// </summary>
        /// <returns></returns>
        public string NetworkInterfaceType { get; set; }

        /// <summary>
        /// 速度
        /// </summary>
        public string Speed { get; set; }
        #endregion

        #region 公用方法

        /// <summary>
        /// 是否是IP地址
        /// </summary>
        /// <param name="ipAddress"></param>
        /// <returns></returns>
        public bool IsIPAddress(string ipAddress)
        {
            string regexStr = @"^(25[0-5]|2[0-4][0-9]|[0-1]{1}[0-9]{2}|[1-9]{1}[0-9]{1}|[1-9])\.(25[0-5]|2[0-4][0-9]|[0-1]{1}[0-9]{2}|[1-9]{1}[0-9]{1}|[1-9]|0)\.(25[0-5]|2[0-4][0-9]|[0-1]{1}[0-9]{2}|[1-9]{1}[0-9]{1}|[1-9]|0)\.(25[0-5]|2[0-4][0-9]|[0-1]{1}[0-9]{2}|[1-9]{1}[0-9]{1}|[0-9])$";
            Match regex = Regex.Match(ipAddress, regexStr);
            if (regex.Success)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// 启用DHCP服务
        /// </summary>
        public bool EnableDHCP()
        {

            ManagementClass wmi = new ManagementClass("Win32_NetworkAdapterConfiguration");
            ManagementObjectCollection moc = wmi.GetInstances();
            foreach (ManagementObject mo in moc)
            {
                if (!(bool)mo["IPEnabled"])
                    continue;

                if (mo["SettingID"].ToString() == this.NetworkInterfaceID) //网卡接口标识是否相等
                {
                    mo.InvokeMethod("SetDNSServerSearchOrder", null);
                    mo.InvokeMethod("EnableDHCP", null);

                }
            }

            NetworkAdapter networkAdapter = new NetworkAdapterUtil().GetNeworkAdapterByNetworkInterfaceID(this.NetworkInterfaceID); //查询现适配器接口信息
            if (networkAdapter != null)
            {
                return networkAdapter.IsDhcpEnabled;
            }
            return false;
        }

        /// <summary>
        /// 设置IP地址,子网掩码，网关,DNS,
        /// </summary>
        public bool SetIPAddressSubMaskDnsGetway(string ipAddress, string subMask, string getWay, string dnsMain, string dnsBackup)
        {
            string[] dnsArray;
            if (string.IsNullOrEmpty(dnsBackup))
            {
                dnsArray = new string[] { dnsMain };
            }
            else
            {
                dnsArray = new string[] { dnsMain, dnsBackup };
            }
            return SetIPAddress(new string[] { ipAddress }, new string[] { subMask }, new string[] { getWay }, dnsArray);

        }

        /// <summary>
        /// 设置IP地址和子网掩码
        /// </summary>
        public bool SetIPAddressAndSubMask(string ipAddress, string subMask)
        {
            return SetIPAddress(new string[] { ipAddress }, new string[] { subMask }, null, null);

        }


        /// <summary>
        /// 设置IP网关
        /// </summary>
        public bool SetGetWayAddress(string getWay)
        {
            return SetIPAddress(null, null, new string[] { getWay }, null);

        }

        /// <summary>
        /// 设置主,备份DNS地址
        /// </summary>
        public bool SetDNSAddress(string dnsMain, string dnsBackup)
        {
            string[] dnsArray;
            if (string.IsNullOrEmpty(dnsBackup))
            {
                dnsArray = new string[] { dnsMain };
            }
            else
            {
                dnsArray = new string[] { dnsMain, dnsBackup };
            }
            return SetIPAddress(null, null, null, dnsArray);

        }

        #endregion

        #region 私用方法

        /// <summary>
        /// 得到IPV4地址
        /// </summary>
        /// <returns></returns>
        private UnicastIPAddressInformation GetAddress()
        {
            if (IPAddresses != null && IPAddresses.Count > 0)
            {
                if (IPAddresses.Count == 3)
                {
                    return IPAddresses[2];
                }
                else if (IPAddresses.Count == 2)
                {
                    return IPAddresses[1];
                }
                else
                {
                    return IPAddresses[0];
                }

            }
            else
            {
                return null;
            }
        }



        /// <summary>
        /// 检查设置IP地址,如果返回空，表示检查通过，为了方便返回就字符串了，没用枚举了
        /// </summary>

        public string IsIPAddress(string ipAddress, string subMask, string getWay, string dnsMain, string dnsBackup)
        {
            if (!string.IsNullOrEmpty(ipAddress))
            {
                if (!IsIPAddress(ipAddress)) return "IP地址格式不对";
            }
            if (!string.IsNullOrEmpty(subMask))
            {
                if (!IsIPAddress(subMask)) return "子网掩码格式不对";
            }
            if (!string.IsNullOrEmpty(getWay))
            {
                if (!IsIPAddress(getWay)) return "网关地址格式不对";
            }
            if (!string.IsNullOrEmpty(dnsMain))
            {
                if (!IsIPAddress(dnsMain)) return "主DNS地址格式不对";
            }
            if (!string.IsNullOrEmpty(dnsBackup))
            {
                if (!IsIPAddress(dnsBackup)) return "备用DNS地址格式不对";
            }
            return "";
        }



        /// <summary>
        /// 设置IP地址
        /// </summary>

        private bool SetIPAddress(string[] ip, string[] submask, string[] getway, string[] dns)
        {
            ManagementClass wmi = new ManagementClass("Win32_NetworkAdapterConfiguration");
            ManagementObjectCollection moc = wmi.GetInstances();
            ManagementBaseObject inPar = null;
            ManagementBaseObject outPar = null;
            string str = "";
            foreach (ManagementObject mo in moc)
            {

                if (!(bool)mo["IPEnabled"])
                    continue;


                if (this.NetworkInterfaceID == mo["SettingID"].ToString())
                {
                    if (ip != null && submask != null)
                    {
                        string caption = mo["Caption"].ToString(); //描述
                        inPar = mo.GetMethodParameters("EnableStatic");
                        inPar["IPAddress"] = ip;
                        inPar["SubnetMask"] = submask;
                        outPar = mo.InvokeMethod("EnableStatic", inPar, null);
                        str = outPar["returnvalue"].ToString();
                        return (str == "0" || str == "1") ? true : false;
                        //获取操作设置IP的返回值， 可根据返回值去确认IP是否设置成功。 0或1表示成功

                    }
                    if (getway != null)
                    {
                        inPar = mo.GetMethodParameters("SetGateways");
                        inPar["DefaultIPGateway"] = getway;
                        outPar = mo.InvokeMethod("SetGateways", inPar, null);
                        str = outPar["returnvalue"].ToString();
                        return (str == "0" || str == "1") ? true : false;
                    }
                    if (dns != null)
                    {
                        inPar = mo.GetMethodParameters("SetDNSServerSearchOrder");
                        inPar["DNSServerSearchOrder"] = dns;
                        outPar = mo.InvokeMethod("SetDNSServerSearchOrder", inPar, null);
                        str = outPar["returnvalue"].ToString();
                        return (str == "0" || str == "1") ? true : false;
                    }

                }
            }
            return false;
        }

        #endregion

    }
}
