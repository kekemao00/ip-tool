using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IP_UpdateTest.Core.TerminalProxy
{
    public enum ShellKind
    {
        PowerShell,
        Cmd,
        Bash
    }

    /// <summary>
    /// 让已打开的终端立即生效的命令（环境变量只对新开的进程生效）
    /// </summary>
    public static class ShellSnippets
    {
        private static readonly char[] BreakAfter = { ' ', ',', ';' };

        /// <summary>
        /// 设置代理；url 为 null 时返回清除代理的命令
        /// </summary>
        public static string Build(ShellKind shell, string url, string noProxy = TerminalProxyAnalyzer.DefaultNoProxy)
        {
            if (url == null) return BuildVariables(shell, TerminalProxyAnalyzer.ProxyVariables.Select(n => new KeyValuePair<string, string>(n, null)));
            var lines = new List<string>();
            switch (shell)
            {
                case ShellKind.PowerShell:
                    lines.Add(PowerShellSet(TerminalProxyAnalyzer.HttpProxy, url));
                    lines.Add(PowerShellSet(TerminalProxyAnalyzer.HttpsProxy, url));
                    lines.Add(PowerShellSet(TerminalProxyAnalyzer.NoProxy, noProxy));
                    break;

                case ShellKind.Cmd:
                    lines.Add(CmdSet(TerminalProxyAnalyzer.HttpProxy, url));
                    lines.Add(CmdSet(TerminalProxyAnalyzer.HttpsProxy, url));
                    lines.Add(CmdSet(TerminalProxyAnalyzer.NoProxy, noProxy));
                    break;

                default:
                    // Git Bash 里的程序区分大小写，curl 只认小写的 http_proxy。
                    // 不标 WSL：WSL2 默认的 NAT 网络里 127.0.0.1 是虚拟机自己，连不到 Windows 上的代理。
                    // 同一条 export 里的 $变量 在赋值前就展开了，所以分成几条
                    lines.Add($"export http_proxy={BashQuote(url)}");
                    lines.Add("export https_proxy=$http_proxy HTTP_PROXY=$http_proxy HTTPS_PROXY=$http_proxy");
                    lines.Add($"export no_proxy={BashQuote(noProxy)}; export NO_PROXY=$no_proxy");
                    break;
            }
            return Join(lines);
        }

        /// <summary>
        /// 把这些环境变量设成给定的值，值为 null 的删除：清除时让已打开的终端和新开的一致（可能恢复了原来的代理）
        /// </summary>
        public static string BuildVariables(ShellKind shell, IEnumerable<KeyValuePair<string, string>> values)
        {
            var lines = new List<string>();
            var removed = new List<string>();
            foreach (KeyValuePair<string, string> v in values)
            {
                if (v.Value == null)
                {
                    removed.Add(v.Key);
                    continue;
                }
                switch (shell)
                {
                    case ShellKind.PowerShell: lines.Add(PowerShellSet(v.Key, v.Value)); break;
                    case ShellKind.Cmd: lines.Add(CmdSet(v.Key, v.Value)); break;
                    default: lines.Add($"export {v.Key.ToLowerInvariant()}={BashQuote(v.Value)} {v.Key}={BashQuote(v.Value)}"); break;
                }
            }
            if (removed.Count > 0)
            {
                switch (shell)
                {
                    case ShellKind.PowerShell:
                        // 每行两个，放得下
                        for (int i = 0; i < removed.Count; i += 2)
                            lines.Add("Remove-Item " + string.Join(", ", removed.Skip(i).Take(2).Select(n => "Env:" + n)) + " -ErrorAction SilentlyContinue");
                        break;
                    case ShellKind.Cmd:
                        lines.AddRange(removed.Select(n => $"set {n}="));
                        break;
                    default:
                        lines.Add("unset " + string.Join(" ", removed.Select(n => n.ToLowerInvariant()).Concat(removed)));
                        break;
                }
            }
            return Join(lines);
        }

        /// <summary>
        /// 把一行命令按列数折行（命令框用等宽字体）：尽量在空格、逗号、分号后断开，续行缩进两格
        /// </summary>
        public static List<string> Wrap(string line, int columns)
        {
            var lines = new List<string>();
            string rest = line ?? "", indent = "";
            columns = Math.Max(columns, 8);
            while (indent.Length + rest.Length > columns)
            {
                int fit = columns - indent.Length;
                int cut = rest.LastIndexOfAny(BreakAfter, fit - 1) + 1;
                if (cut < fit / 3) cut = fit;
                lines.Add(indent + rest.Substring(0, cut).TrimEnd());
                rest = rest.Substring(cut).TrimStart();
                indent = "  ";
            }
            if (rest.Length > 0 || lines.Count == 0) lines.Add(indent + rest);
            return lines;
        }

        public static string NameOf(ShellKind shell)
        {
            switch (shell)
            {
                case ShellKind.PowerShell: return "PowerShell";
                case ShellKind.Cmd: return "cmd";
                default: return "Git Bash";
            }
        }

        // 单引号里什么都不展开，值里的单引号写两个
        private static string PowerShellSet(string name, string value)
        {
            return $"$env:{name} = '{value.Replace("'", "''")}'";
        }

        // 值里有 &、|、<、> 等时整个赋值加引号，引号不会进到值里
        private static string CmdSet(string name, string value)
        {
            return value.IndexOfAny(new[] { '&', '|', '<', '>', '^', '"', '(', ')' }) >= 0 || value != value.Trim()
                ? $"set \"{name}={value}\""
                : $"set {name}={value}";
        }

        // 只含常见字符时原样写，否则加单引号（如用户已有的 NO_PROXY 里有空格、分号）
        private static string BashQuote(string value)
        {
            bool plain = value.Length > 0 && value.All(c => char.IsLetterOrDigit(c) && c < 128 || "_.,:/@%+=-".IndexOf(c) >= 0);
            return plain ? value : "'" + value.Replace("'", "'\\''") + "'";
        }

        private static string Join(List<string> lines)
        {
            var text = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0) text.Append("\r\n");
                text.Append(lines[i]);
            }
            return text.ToString();
        }
    }
}
