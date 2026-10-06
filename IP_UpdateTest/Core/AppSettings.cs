using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Security;
using Microsoft.Win32;

namespace IP_UpdateTest.Core
{
    /// <summary>
    /// 用户自定义的 DNS 预设
    /// </summary>
    [DataContract]
    public sealed class DnsPreset
    {
        [DataMember] public string Name { get; set; }
        [DataMember] public string[] Servers { get; set; }
    }

    /// <summary>
    /// 程序设置，保存在 %APPDATA%\IPTool\settings.json
    /// </summary>
    [DataContract]
    public sealed class AppSettings
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IPTool", "settings.json");

        [DataMember] private List<DnsPreset> customDnsPresets;

        /// <summary>
        /// 点关闭时隐藏到托盘而不是退出
        /// </summary>
        [DataMember] public bool MinimizeToTray { get; set; }

        /// <summary>
        /// 网卡列表同时显示虚拟网卡
        /// </summary>
        [DataMember] public bool ShowAllAdapters { get; set; }

        /// <summary>
        /// 是否已提示过“程序仍在托盘运行”
        /// </summary>
        [DataMember] public bool TrayHintShown { get; set; }

        public List<DnsPreset> CustomDnsPresets
        {
            get { return customDnsPresets ?? (customDnsPresets = new List<DnsPreset>()); }
        }

        /// <summary>
        /// 读取设置；文件不存在或损坏时使用默认设置
        /// </summary>
        public static AppSettings Load()
        {
            try
            {
                return JsonFile.Read<AppSettings>(SettingsPath) ?? new AppSettings();
            }
            catch (Exception ex) when (ex is SerializationException || ex is IOException || ex is UnauthorizedAccessException)
            {
                return new AppSettings();
            }
        }

        public void Save()
        {
            JsonFile.WriteAtomic(SettingsPath, this);
        }
    }

    /// <summary>
    /// 开机自动启动（当前用户的 Run 注册表项，启动后只显示托盘图标）
    /// </summary>
    public static class AutoStart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "IPTool";

        /// <summary>
        /// 开机启动时附加的参数：直接缩到托盘
        /// </summary>
        public const string TrayArgument = "--tray";

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                    {
                        return key != null && key.GetValue(ValueName) is string;
                    }
                }
                catch (SecurityException)
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// 已开启自启动但登记的程序已不存在（如从旧版 IP_UpdateTest.exe 换成 IPTool.exe）时，改为指向当前程序
        /// </summary>
        public static void RefreshPath(string executablePath)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    string command = key?.GetValue(ValueName) as string;
                    if (command == null) return;
                    string registered = ParseExecutable(command);
                    if (registered != null && File.Exists(registered)) return;
                    key.SetValue(ValueName, $"\"{executablePath}\" {TrayArgument}");
                }
            }
            catch (Exception ex) when (ex is SecurityException || ex is UnauthorizedAccessException || ex is IOException)
            {
            }
        }

        /// <summary>
        /// 取 Run 项命令中的程序路径：带引号时取引号内，否则取第一个空格前
        /// </summary>
        internal static string ParseExecutable(string command)
        {
            command = command.Trim();
            if (command.StartsWith("\""))
            {
                int end = command.IndexOf('"', 1);
                return end > 1 ? command.Substring(1, end - 1) : null;
            }
            int space = command.IndexOf(' ');
            return space < 0 ? command : command.Substring(0, space);
        }

        public static void Set(bool enable, string executablePath)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enable)
                    key.SetValue(ValueName, $"\"{executablePath}\" {TrayArgument}");
                else
                    key.DeleteValue(ValueName, false);
            }
        }
    }
}
