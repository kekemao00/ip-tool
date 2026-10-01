using System.Collections.Generic;

namespace IP_UpdateTest.Core
{
    /// <summary>
    /// Win32_NetworkAdapterConfiguration 方法（EnableStatic/SetGateways/SetDNSServerSearchOrder/EnableDHCP）的返回值
    /// 参考：https://learn.microsoft.com/windows/win32/cimwin32prov/enablestatic-method-in-class-win32-networkadapterconfiguration
    /// </summary>
    public static class WmiReturnCode
    {
        public const uint Success = 0;
        public const uint SuccessRebootRequired = 1;
        public const uint UnableToConfigureDhcp = 81;
        public const uint IpNotEnabled = 84;
        public const uint AccessDenied = 91;

        private static readonly Dictionary<uint, string> Messages = new Dictionary<uint, string>
        {
            { 0, "成功" },
            { 1, "成功（需要重启）" },
            { 64, "此平台不支持该方法" },
            { 65, "未知错误" },
            { 66, "子网掩码无效" },
            { 67, "处理返回的实例时出错" },
            { 68, "输入参数无效" },
            { 69, "指定的网关超过 5 个" },
            { 70, "IP 地址无效" },
            { 71, "网关地址无效" },
            { 72, "访问注册表时出错" },
            { 73, "域名无效" },
            { 74, "主机名无效" },
            { 75, "未定义主/辅 WINS 服务器" },
            { 76, "文件无效" },
            { 77, "系统路径无效" },
            { 78, "文件复制失败" },
            { 79, "安全参数无效" },
            { 80, "无法配置 TCP/IP 服务" },
            { 81, "无法配置 DHCP 服务" },
            { 82, "无法续订 DHCP 租约" },
            { 83, "无法释放 DHCP 租约" },
            { 84, "网卡未启用 IP（网卡未连接或已禁用）" },
            { 85, "网卡未启用 IPX" },
            { 86, "帧/网络号越界" },
            { 87, "帧类型无效" },
            { 88, "网络号无效" },
            { 89, "网络号重复" },
            { 90, "参数越界" },
            { 91, "拒绝访问，需要管理员权限" },
            { 92, "内存不足" },
            { 93, "已存在" },
            { 94, "找不到路径、文件或对象" },
            { 95, "无法通知服务" },
            { 96, "无法通知 DNS 服务" },
            { 97, "接口不可配置" },
            { 98, "部分 DHCP 租约无法释放或续订" },
            { 100, "网卡未启用 DHCP" },
            { 2147786788, "未获得网络配置写锁，请关闭其他网络设置窗口后重试" },
        };

        /// <summary>
        /// 判断方法调用是否成功
        /// </summary>
        public static bool IsSuccess(string method, uint code)
        {
            if (code == Success || code == SuccessRebootRequired) return true;

            // 文档说明：网卡已是静态地址时 EnableStatic 返回 81，但设置实际已生效
            return code == UnableToConfigureDhcp && method == "EnableStatic";
        }

        /// <summary>
        /// 返回值的中文说明，如“拒绝访问，需要管理员权限（错误码 91）”
        /// </summary>
        public static string Describe(uint code)
        {
            string text;
            if (!Messages.TryGetValue(code, out text)) text = "未知错误";
            return $"{text}（错误码 {code}）";
        }
    }
}
