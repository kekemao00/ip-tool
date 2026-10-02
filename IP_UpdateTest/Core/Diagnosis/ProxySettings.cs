using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using Microsoft.Win32;

namespace IP_UpdateTest.Core.Diagnosis
{
    /// <summary>
    /// 读取各处的代理设置：系统代理（浏览器等使用）、PAC 脚本、WinHTTP 代理（系统服务使用）、环境变量（命令行工具使用）
    /// </summary>
    public static class ProxySettings
    {
        private const string InternetSettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
        private const string WinHttpKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Internet Settings\Connections";

        public static readonly string[] EnvironmentVariables = { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY" };

        /// <summary>
        /// 读取当前用户的系统代理与 PAC 设置，以及 WinHTTP 代理和环境变量；Endpoint 为待测试的 host:port
        /// </summary>
        public static List<ProxyFact> Read(out bool systemProxyEnabled)
        {
            var facts = new List<ProxyFact>();
            systemProxyEnabled = false;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey))
                {
                    if (key != null)
                    {
                        bool enabled = key.GetValue("ProxyEnable") is int flag && flag != 0;
                        string server = key.GetValue("ProxyServer") as string;
                        if (enabled && !string.IsNullOrWhiteSpace(server))
                        {
                            systemProxyEnabled = true;
                            facts.Add(new ProxyFact { Source = "系统代理", Value = server.Trim(), Endpoint = FirstEndpoint(server) });
                        }

                        string pac = key.GetValue("AutoConfigURL") as string;
                        if (!string.IsNullOrWhiteSpace(pac))
                        {
                            systemProxyEnabled = true;
                            facts.Add(new ProxyFact { Source = "PAC 脚本", Value = pac.Trim(), Endpoint = UrlEndpoint(pac) });
                        }
                    }
                }

                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(WinHttpKey))
                {
                    string winHttp = ParseWinHttpSettings(key?.GetValue("WinHttpSettings") as byte[]);
                    if (winHttp != null)
                        facts.Add(new ProxyFact { Source = "WinHTTP 代理", Value = winHttp, Endpoint = FirstEndpoint(winHttp) });
                }
            }
            catch (Exception ex) when (ex is SecurityException || ex is IOException || ex is UnauthorizedAccessException)
            {
                // 读不到时按未设置处理
            }

            foreach (string name in EnvironmentVariables)
            {
                string value = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrWhiteSpace(value))
                    facts.Add(new ProxyFact { Source = "环境变量 " + name, Value = value.Trim(), Endpoint = UrlEndpoint(value) });
            }
            return facts;
        }

        /// <summary>
        /// 系统代理的 ProxyServer 可以是“host:port”，也可以按协议分别设置，如“http=127.0.0.1:7890;https=127.0.0.1:7890;socks=127.0.0.1:7891”。
        /// 返回其中的 host:port（未写端口时 WinINet 默认 80），去重保持顺序。
        /// </summary>
        public static List<string> ParseServerList(string value)
        {
            var endpoints = new List<string>();
            if (string.IsNullOrWhiteSpace(value)) return endpoints;
            foreach (string part in value.Split(new[] { ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string entry = part.Trim();
                int eq = entry.IndexOf('=');
                if (eq >= 0) entry = entry.Substring(eq + 1);
                string endpoint = UrlEndpoint(entry.Contains("://") ? entry : "http://" + entry);
                if (endpoint != null && !endpoints.Contains(endpoint)) endpoints.Add(endpoint);
            }
            return endpoints;
        }

        public static string FirstEndpoint(string serverList)
        {
            return ParseServerList(serverList).FirstOrDefault();
        }

        /// <summary>
        /// 由 URL（如 http://127.0.0.1:7890、socks5://host:1080、PAC 地址）取 host:port；无法解析时返回 null
        /// </summary>
        public static string UrlEndpoint(string url)
        {
            Uri uri;
            if (string.IsNullOrWhiteSpace(url)) return null;
            string text = url.Trim();
            if (!text.Contains("://")) text = "http://" + text;
            if (!Uri.TryCreate(text, UriKind.Absolute, out uri) || string.IsNullOrEmpty(uri.Host) || uri.IsFile) return null;
            int port = uri.Port > 0 ? uri.Port : uri.Scheme.StartsWith("socks", StringComparison.OrdinalIgnoreCase) ? 1080 : 80;
            return uri.Host + ":" + port;
        }

        /// <summary>
        /// 解析注册表 WinHttpSettings（netsh winhttp set proxy 写入）：
        /// 4 字节结构版本、4 字节计数、4 字节标志（含 0x2 表示使用代理）、4 字节长度 + 代理地址、4 字节长度 + 例外列表。
        /// 未设置代理（直连）时返回 null。
        /// </summary>
        public static string ParseWinHttpSettings(byte[] data)
        {
            if (data == null || data.Length < 16) return null;
            int flags = BitConverter.ToInt32(data, 8);
            int length = BitConverter.ToInt32(data, 12);
            if ((flags & 0x2) == 0 || length <= 0 || 16 + length > data.Length) return null;
            string proxy = Encoding.ASCII.GetString(data, 16, length).Trim('\0', ' ');
            return proxy.Length == 0 ? null : proxy;
        }

        /// <summary>
        /// 拆分 host:port
        /// </summary>
        public static bool TrySplit(string endpoint, out string host, out int port)
        {
            host = null;
            port = 0;
            if (string.IsNullOrEmpty(endpoint)) return false;
            int colon = endpoint.LastIndexOf(':');
            if (colon <= 0 || !int.TryParse(endpoint.Substring(colon + 1), out port)) return false;
            host = endpoint.Substring(0, colon).Trim('[', ']');
            return port > 0 && port < 65536;
        }
    }
}
