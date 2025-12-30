using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Text;

namespace IP_UpdateTest
{
    /// <summary>
    /// 网络适配器工具类，可获取指定网络适配器，集合
    /// </summary>
    public class NetworkAdapterUtil
    {
        /// <summary>
        /// 用于储存适配器集合
        /// </summary>
        List<NetworkAdapter> adapterList;


        /// <summary>
        /// 获取电脑适配器个数
        /// </summary>
        /// <returns>总个数</returns>
        public int GetCount()
        {
            if (adapterList == null)
            {
                // GetAllNetworkAdaptersUPAndDown();
                GetAllNetworkAdapters();
                return 0;
            }
            return adapterList.Count;
        }

        /// <summary>
        /// 获取 Ethernet,Wireless80211 UP适配器，适配器被禁用则不能获取到
        /// </summary>
        /// <returns></returns>
        public List<NetworkAdapter> GetEthernetWirelessNetworkAdaptersUP()
        {
            //获得所有UP适配器
            IEnumerable<NetworkInterface> adapters = NetworkInterface.GetAllNetworkInterfaces().Where(d => d.OperationalStatus == OperationalStatus.Up);
            return GetNetworkAdapters(adapters, NetworkInterfaceType.Ethernet, NetworkInterfaceType.Wireless80211);
        }


        /// <summary>
        /// 获取所有适配器类型，适配器被禁用则不能获取到
        /// </summary>
        /// <returns></returns>
        public List<NetworkAdapter> GetAllNetworkAdapters() //如果适配器被禁用则不能获取到
        {
            IEnumerable<NetworkInterface> adapters = NetworkInterface.GetAllNetworkInterfaces();
            return GetNetworkAdapters(adapters);

        }

        /// <summary>
        /// 获取所有UP适配器类型
        /// </summary>
        /// <returns></returns>
        public List<NetworkAdapter> GetAllNetworkAdaptersUPAddress()
        {
            IEnumerable<NetworkInterface> adapters = NetworkInterface.GetAllNetworkInterfaces().Where(d => d.OperationalStatus == OperationalStatus.Up);
            return GetNetworkAdapters(adapters);

        }

        /// <summary>
        /// 获取所有DOWN适配器类型
        /// </summary>
        /// <returns></returns>
        public List<NetworkAdapter> GetAllNetworkAdaptersDown()
        {
            IEnumerable<NetworkInterface> adapters = NetworkInterface.GetAllNetworkInterfaces().Where(d => d.OperationalStatus == OperationalStatus.Down);
            return GetNetworkAdapters(adapters);

        }

        /// <summary>
        /// 根据适配器ID得到适配器信息
        /// </summary>
        /// <param name="networkInterfaceID"></param>
        /// <returns></returns>
        public NetworkAdapter GetNeworkAdapterByNetworkInterfaceID(string networkInterfaceID)
        {
            IEnumerable<NetworkInterface> adapters = NetworkInterface.GetAllNetworkInterfaces();

            foreach (NetworkInterface adapter in adapters)
            {
                NetworkAdapter network = SetNetworkAdapterValue(adapter);
                if (network.NetworkInterfaceID == networkInterfaceID)
                {
                    return network;
                }
            }
            return null;
        }


        /// <summary>
        /// 启用所有适配器
        /// </summary>
        /// <returns></returns>
        public void EnableAllAdapters()
        {
            // ManagementClass wmi = new ManagementClass("Win32_NetworkAdapter");
            // ManagementObjectCollection moc = wmi.GetInstances();
            System.Management.ManagementObjectSearcher moc = new System.Management.ManagementObjectSearcher("Select * from Win32_NetworkAdapter where NetEnabled!=null ");
            foreach (System.Management.ManagementObject mo in moc.Get())
            {
                //if (!(bool)mo["NetEnabled"])
                //    continue;
                string capation = mo["Caption"].ToString();
                string descrption = mo["Description"].ToString();
                mo.InvokeMethod("Enable", null);
            }

        }

        /// <summary>
        /// 禁用所有适配器
        /// </summary>
        public void DisableAllAdapters()
        {
            // ManagementClass wmi = new ManagementClass("Win32_NetworkAdapter");
            // ManagementObjectCollection moc = wmi.GetInstances();
            System.Management.ManagementObjectSearcher moc = new System.Management.ManagementObjectSearcher("Select * from Win32_NetworkAdapter where NetEnabled!=null ");
            foreach (System.Management.ManagementObject mo in moc.Get())
            {
                //if ((bool)mo["NetEnabled"])
                //    continue;
                string capation = mo["Caption"].ToString();
                string descrption = mo["Description"].ToString();
                mo.InvokeMethod("Disable", null);
            }

        }

        /// <summary>
        /// 根据适配器生成报表
        /// </summary>
        /// <param name="adapters"></param>
        /// <returns></returns>
        public string ReportByAdapters(List<NetworkAdapter> adapters)
        {
            if (adapters == null || adapters.Count <= 0)
            {
                return "没有可用适配器";
            }

            StringBuilder sb = new StringBuilder();
            int index = 0;
            sb.AppendLine("适配器总数: " + adapters.Count);

            foreach (NetworkAdapter adapter in adapters)
            {
                index++;
                sb.AppendLine("---------------------第" + index + "个适配器信息---------------------");
                sb.AppendLine("描述信息:    " + adapter.Description);
                sb.AppendLine("接口类型:    " + adapter.NetworkInterfaceType);
                sb.AppendLine("速度:        " + adapter.Speed);
                sb.AppendLine("获取IP方式:  " + (adapter.IsDhcpEnabled ? "自动" : "手动"));
                sb.AppendLine("IP地址:      " + adapter.IpAddress);
                sb.AppendLine("子网掩码:    " + adapter.Mask);
                sb.AppendLine("网关:        " + adapter.Getway);
                sb.AppendLine("主DNS地址:   " + adapter.DnsMain);
                sb.AppendLine("备用DNS地址: " + adapter.DnsBackup);
                if (adapter.IsDhcpEnabled) sb.AppendLine("DHCP服务器:  " + adapter.DhcpServer);
                sb.AppendLine("MAC地址:     " + adapter.MacAddres);

                sb.AppendLine();

            }
            return sb.ToString();
        }



        /// <summary>
        /// 根据条件获取IP地址集合，
        /// </summary>
        /// <param name="adapters">网络接口地址集合</param>
        /// <param name="adapterTypes">网络连接状态，如,UP,DOWN等</param>
        /// <returns></returns>
        private List<NetworkAdapter> GetNetworkAdapters(IEnumerable<NetworkInterface> adapters, params NetworkInterfaceType[] networkInterfaceTypes)
        {
            adapterList = new List<NetworkAdapter>();

            foreach (NetworkInterface adapter in adapters)
            {
                if (networkInterfaceTypes.Length <= 0) //如果没传可选参数，就查询所有
                {
                    if (adapter != null)
                    {
                        NetworkAdapter adp = SetNetworkAdapterValue(adapter);
                        adapterList.Add(adp);
                    }
                    else
                    {
                        return null;
                    }
                }
                else //过滤查询数据
                {
                    foreach (NetworkInterfaceType networkInterfaceType in networkInterfaceTypes)
                    {
                        if (adapter.NetworkInterfaceType.ToString().Equals(networkInterfaceType.ToString()))
                        {
                            adapterList.Add(SetNetworkAdapterValue(adapter));
                            break; //退出当前循环
                        }
                    }
                }
            }
            return adapterList;
        }

        /// <summary>
        /// 设置网络适配器信息
        /// </summary>
        /// <param name="adapter"></param>
        /// <returns></returns>
        private NetworkAdapter SetNetworkAdapterValue(NetworkInterface adapter)
        {
            NetworkAdapter networkAdapter = new NetworkAdapter();
            IPInterfaceProperties ips = adapter.GetIPProperties();
            networkAdapter.Description = adapter.Name;
            networkAdapter.NetworkInterfaceType = adapter.NetworkInterfaceType.ToString();
            networkAdapter.Speed = adapter.Speed / 1000 / 1000 + "MB"; //速度
            networkAdapter.MacAddress = adapter.GetPhysicalAddress(); //物理地址集合
            networkAdapter.NetworkInterfaceID = adapter.Id;//网络适配器标识符

            networkAdapter.Getwaryes = ips.GatewayAddresses; //网关地址集合
            networkAdapter.IPAddresses = ips.UnicastAddresses; //IP地址集合
            networkAdapter.DhcpServerAddresses = ips.DhcpServerAddresses;//DHCP地址集合
            networkAdapter.IsDhcpEnabled = ips.GetIPv4Properties() == null ? false : ips.GetIPv4Properties().IsDhcpEnabled; //是否启用DHCP服务

            IPInterfaceProperties adapterProperties = adapter.GetIPProperties();//获取IPInterfaceProperties实例  
            networkAdapter.DnsAddresses = adapterProperties.DnsAddresses; //获取并显示DNS服务器IP地址信息 集合
            return networkAdapter;
        }
    }

}

