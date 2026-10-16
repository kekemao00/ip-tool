using System;
using System.Collections.Generic;
using System.Linq;
using IP_UpdateTest.Core.Diagnosis;

namespace IP_UpdateTest.Core.TerminalProxy
{
    /// <summary>
    /// 本机一个可用作代理的端口
    /// </summary>
    public sealed class ProxyCandidate
    {
        public string Host { get; set; } = "127.0.0.1";
        public int Port { get; set; }
        public bool Http { get; set; }
        public bool Socks5 { get; set; }

        /// <summary>
        /// 监听这个端口的代理软件，如 v2rayN；不认识时为 null
        /// </summary>
        public string Owner { get; set; }

        /// <summary>
        /// 系统代理设置的就是这个端口
        /// </summary>
        public bool IsSystemProxy { get; set; }

        public string Endpoint
        {
            get { return Host + ":" + Port; }
        }

        /// <summary>
        /// 写进环境变量的地址：能用 HTTP 时优先 HTTP（pip、npm 不支持 SOCKS），否则 socks5h（域名也交给代理解析）
        /// </summary>
        public string Url
        {
            get { return (Http || !Socks5 ? "http://" : "socks5h://") + Endpoint; }
        }

        public string Protocol
        {
            get { return Http && Socks5 ? "HTTP + SOCKS5" : Http ? "HTTP" : Socks5 ? "SOCKS5" : "未知协议"; }
        }

        public override string ToString()
        {
            return Endpoint;
        }
    }

    /// <summary>
    /// 会单独设置代理、优先于环境变量的命令行工具
    /// </summary>
    public enum ProxyTool
    {
        Git,
        Npm,
        Pip
    }

    /// <summary>
    /// 一个工具自己的代理设置
    /// </summary>
    public sealed class ToolProxySetting
    {
        public ProxyTool Tool { get; set; }

        /// <summary>
        /// 本机装了这个工具
        /// </summary>
        public bool Installed { get; set; }

        /// <summary>
        /// 设置项 → 值（如 http.proxy → http://127.0.0.1:7890），只含已设置的项
        /// </summary>
        public Dictionary<string, string> Values { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string NameOf(ProxyTool tool)
        {
            switch (tool)
            {
                case ProxyTool.Git: return "Git";
                case ProxyTool.Npm: return "npm";
                default: return "pip";
            }
        }

        /// <summary>
        /// 配置文件里的代理项（Git 另外还有 http.&lt;url&gt;.proxy，见 TerminalProxyScanner）
        /// </summary>
        public static string[] KeysOf(ProxyTool tool)
        {
            switch (tool)
            {
                case ProxyTool.Git: return new[] { "http.proxy", "https.proxy" };
                case ProxyTool.Npm: return new[] { "proxy", "https-proxy" };
                default: return new[] { "proxy" };
            }
        }

        /// <summary>
        /// 工具其实不读的项：Git 只认 http.proxy 和 http.&lt;url&gt;.proxy，很多教程让人设置的 https.proxy 不起作用
        /// </summary>
        public static bool IsIgnored(ProxyTool tool, string key)
        {
            return tool == ProxyTool.Git && string.Equals(key, "https.proxy", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// 检测时采集到的原始数据。采集（TerminalProxyScanner）与分析（TerminalProxyAnalyzer）分开，分析是纯函数，便于测试
    /// </summary>
    public sealed class TerminalProxySnapshot
    {
        public DateTime GeneratedAt { get; set; }

        /// <summary>
        /// 正在运行的代理软件，如 v2rayN、Clash Verge
        /// </summary>
        public List<string> Clients { get; } = new List<string>();

        /// <summary>
        /// 本机可用的代理端口，系统代理的端口排最前
        /// </summary>
        public List<ProxyCandidate> Candidates { get; } = new List<ProxyCandidate>();

        public bool SystemProxyEnabled { get; set; }
        public string SystemProxyServer { get; set; }
        public string PacUrl { get; set; }

        /// <summary>
        /// 系统代理的例外列表（ProxyOverride），如“localhost;127.*;10.*;*.corp.com;&lt;local&gt;”
        /// </summary>
        public string SystemProxyBypass { get; set; }

        /// <summary>
        /// 用户级环境变量（HKCU\Environment）中与代理相关的项
        /// </summary>
        public Dictionary<string, string> UserEnvironment { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 系统级环境变量中与代理相关的项
        /// </summary>
        public Dictionary<string, string> MachineEnvironment { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public List<ToolProxySetting> Tools { get; } = new List<ToolProxySetting>();

        public string WinHttpProxy { get; set; }

        /// <summary>
        /// 检测过的地址 host:port → 能否连接
        /// </summary>
        public Dictionary<string, bool> Reachable { get; } = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 经各代理（host:port）访问外网的耗时毫秒；值为 null 表示访问失败，没有这一项表示没测
        /// </summary>
        public Dictionary<string, long?> Tunnels { get; } = new Dictionary<string, long?>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 新开的终端里实际生效的值：用户级覆盖系统级
        /// </summary>
        public string EffectiveEnvironment(string name)
        {
            string value;
            if (UserEnvironment.TryGetValue(name, out value) && !string.IsNullOrWhiteSpace(value)) return value.Trim();
            if (MachineEnvironment.TryGetValue(name, out value) && !string.IsNullOrWhiteSpace(value)) return value.Trim();
            return null;
        }

        /// <summary>
        /// 地址能否连接；没测过时为 null
        /// </summary>
        public bool? IsReachable(string endpoint)
        {
            bool value;
            if (endpoint == null || !Reachable.TryGetValue(endpoint, out value)) return null;
            return value;
        }
    }

    /// <summary>
    /// 检测报告中的一行
    /// </summary>
    public sealed class TerminalProxyRow
    {
        public TerminalProxyRow(string key, string title, CheckStatus status, string badge, string detail)
        {
            Key = key;
            Title = title;
            Status = status;
            Badge = badge;
            Detail = detail;
        }

        /// <summary>
        /// 界面据此选图标：proxy、env、git、npm、pip、system、winhttp
        /// </summary>
        public string Key { get; private set; }

        public string Title { get; private set; }
        public CheckStatus Status { get; private set; }

        /// <summary>
        /// 右侧的状态标签，如“已生效”“未设置”“已失效”
        /// </summary>
        public string Badge { get; private set; }

        public string Detail { get; private set; }
    }

    /// <summary>
    /// 顶部链路中的一环：代理软件 → 本机端口 → 系统代理 → 命令行
    /// </summary>
    public sealed class TerminalProxyStep
    {
        public TerminalProxyStep(string name, CheckStatus status)
        {
            Name = name;
            Status = status;
        }

        public string Name { get; private set; }
        public CheckStatus Status { get; private set; }
    }

    /// <summary>
    /// 分析结果
    /// </summary>
    public sealed class TerminalProxyReport
    {
        public DateTime GeneratedAt { get; set; }
        public CheckStatus Overall { get; set; }
        public string Title { get; set; }
        public string Detail { get; set; }

        /// <summary>
        /// 一键设置要用的代理；没有可用代理时为 null
        /// </summary>
        public ProxyCandidate Target { get; set; }

        public List<TerminalProxyStep> Steps { get; } = new List<TerminalProxyStep>();
        public List<TerminalProxyRow> Rows { get; } = new List<TerminalProxyRow>();

        /// <summary>
        /// 一键设置会做的修改；为空表示已是最新
        /// </summary>
        public List<ProxyChange> ApplyChanges { get; } = new List<ProxyChange>();

        /// <summary>
        /// 清除会做的修改
        /// </summary>
        public List<ProxyChange> ClearChanges { get; } = new List<ProxyChange>();

        /// <summary>
        /// 清除之后仍然指向失效地址的环境变量，如“HTTPS_PROXY = http://127.0.0.1:7890（系统级）”；清除能恢复正常时为 null
        /// </summary>
        public string StaleAfterClear { get; set; }

        /// <summary>
        /// 命令里写的 NO_PROXY：已有的值，没有时是一键设置会写入的值
        /// </summary>
        public string NoProxy { get; set; }

        public bool HasProblem
        {
            get { return Overall == CheckStatus.Fail || Overall == CheckStatus.Warning; }
        }
    }

    /// <summary>
    /// 设置在哪里
    /// </summary>
    public enum ProxyChangeTarget
    {
        Environment,
        Git,
        Npm,
        Pip
    }

    /// <summary>
    /// 一项修改：NewValue 为 null 表示删除
    /// </summary>
    public sealed class ProxyChange
    {
        public ProxyChange(ProxyChangeTarget target, string key, string oldValue, string newValue)
        {
            Target = target;
            Key = key;
            OldValue = oldValue;
            NewValue = newValue;
        }

        public ProxyChangeTarget Target { get; private set; }
        public string Key { get; private set; }
        public string OldValue { get; private set; }
        public string NewValue { get; private set; }

        public override string ToString()
        {
            string where = Target == ProxyChangeTarget.Environment ? "" : ToolProxySetting.NameOf((ProxyTool)(Target - 1)) + " ";
            return NewValue == null ? $"删除 {where}{Key}" : $"{where}{Key} = {NewValue}";
        }
    }

    /// <summary>
    /// 一键设置改过的项：改之前的值（清除时据此恢复），以及本程序写入的值（据此判断现在的值是不是本程序设的，
    /// 用户之后自己改过的不动）。键为环境变量名，或“Git:http.proxy”这样的工具设置项。保存在 %APPDATA%\IPTool\terminal-proxy.json
    /// </summary>
    [System.Runtime.Serialization.DataContract]
    public sealed class TerminalProxyBackup
    {
        [System.Runtime.Serialization.DataMember] private Dictionary<string, string> previous;
        [System.Runtime.Serialization.DataMember] private Dictionary<string, string> written;

        /// <summary>
        /// 键 → 本程序第一次改它之前的值（之前没有的不记录）
        /// </summary>
        public Dictionary<string, string> Previous
        {
            get { return previous ?? (previous = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)); }
        }

        /// <summary>
        /// 键 → 本程序最后写入的值
        /// </summary>
        public Dictionary<string, string> Written
        {
            get { return written ?? (written = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)); }
        }

        public bool IsEmpty
        {
            get { return Previous.Count == 0 && Written.Count == 0; }
        }

        public static string KeyOf(ProxyChangeTarget target, string key)
        {
            return target == ProxyChangeTarget.Environment ? key : target + ":" + key;
        }

        /// <summary>
        /// 现在的值是本程序写入的
        /// </summary>
        public bool Wrote(string key, string current)
        {
            string value = Find(Written, key);
            return value != null && current != null && string.Equals(value.Trim().TrimEnd('/'), current.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
        }

        public string PreviousOf(string key)
        {
            return Find(Previous, key);
        }

        /// <summary>
        /// 写入前调用：第一次写（或用户在两次写入之间自己改过）时记下原来的值
        /// </summary>
        public void Record(string key, string oldValue, string newValue)
        {
            if (Find(Written, key) == null || !Wrote(key, oldValue))
            {
                Remove(Previous, key);
                if (!string.IsNullOrWhiteSpace(oldValue)) Previous[key] = oldValue.Trim();
            }
            Remove(Written, key);
            Written[key] = newValue;
        }

        /// <summary>
        /// 清除（恢复原值）之后不再需要这一项
        /// </summary>
        public void Forget(string key)
        {
            Remove(Previous, key);
            Remove(Written, key);
        }

        // 反序列化得到的字典区分大小写，按不区分大小写查找
        private static string Find(Dictionary<string, string> values, string key)
        {
            foreach (KeyValuePair<string, string> pair in values)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) return string.IsNullOrWhiteSpace(pair.Value) ? null : pair.Value;
            }
            return null;
        }

        private static void Remove(Dictionary<string, string> values, string key)
        {
            foreach (string k in values.Keys.Where(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)).ToList()) values.Remove(k);
        }
    }
}
