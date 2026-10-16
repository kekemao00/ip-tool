using System.Collections.Generic;
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
        /// <summary>
        /// 设置代理；url 为 null 时返回清除代理的命令
        /// </summary>
        public static string Build(ShellKind shell, string url, string noProxy = TerminalProxyAnalyzer.DefaultNoProxy)
        {
            var lines = new List<string>();
            switch (shell)
            {
                case ShellKind.PowerShell:
                    if (url == null)
                    {
                        lines.Add("Remove-Item Env:HTTP_PROXY, Env:HTTPS_PROXY -ErrorAction SilentlyContinue");
                        lines.Add("Remove-Item Env:ALL_PROXY -ErrorAction SilentlyContinue");
                        break;
                    }
                    lines.Add($"$env:HTTP_PROXY = \"{url}\"");
                    lines.Add($"$env:HTTPS_PROXY = \"{url}\"");
                    lines.Add($"$env:NO_PROXY = \"{noProxy}\"");
                    break;

                case ShellKind.Cmd:
                    if (url == null)
                    {
                        lines.Add("set HTTP_PROXY=");
                        lines.Add("set HTTPS_PROXY=");
                        lines.Add("set ALL_PROXY=");
                        break;
                    }
                    lines.Add($"set HTTP_PROXY={url}");
                    lines.Add($"set HTTPS_PROXY={url}");
                    lines.Add($"set NO_PROXY={noProxy}");
                    break;

                default:
                    // Git Bash 里的程序区分大小写，curl 只认小写的 http_proxy。
                    // 不标 WSL：WSL2 默认的 NAT 网络里 127.0.0.1 是虚拟机自己，连不到 Windows 上的代理
                    if (url == null)
                    {
                        lines.Add("unset http_proxy https_proxy all_proxy HTTP_PROXY HTTPS_PROXY ALL_PROXY");
                        break;
                    }
                    // 同一条 export 里的 $变量 在赋值前就展开了，所以分成几条
                    lines.Add($"export http_proxy={url}");
                    lines.Add("export https_proxy=$http_proxy HTTP_PROXY=$http_proxy HTTPS_PROXY=$http_proxy");
                    lines.Add($"export no_proxy={noProxy}; export NO_PROXY=$no_proxy");
                    break;
            }
            var text = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0) text.Append("\r\n");
                text.Append(lines[i]);
            }
            return text.ToString();
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
    }
}
