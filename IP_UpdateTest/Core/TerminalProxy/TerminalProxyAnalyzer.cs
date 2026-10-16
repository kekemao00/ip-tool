using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using IP_UpdateTest.Core.Diagnosis;

namespace IP_UpdateTest.Core.TerminalProxy
{
    /// <summary>
    /// 由检测数据得出结论：命令行有没有走代理、哪里不对、一键设置和清除各要改什么。纯函数
    /// </summary>
    public static class TerminalProxyAnalyzer
    {
        public const string HttpProxy = "HTTP_PROXY";
        public const string HttpsProxy = "HTTPS_PROXY";
        public const string AllProxy = "ALL_PROXY";
        public const string NoProxy = "NO_PROXY";

        /// <summary>
        /// 一键设置写入的环境变量（NO_PROXY 只在原来没有时写入）
        /// </summary>
        public static readonly string[] ProxyVariables = { HttpProxy, HttpsProxy, AllProxy };

        public static readonly string[] AllVariables = { HttpProxy, HttpsProxy, AllProxy, NoProxy };

        /// <summary>
        /// 本机和局域网地址不走代理
        /// </summary>
        public const string DefaultNoProxy = "localhost,127.0.0.1,::1";

        /// <summary>
        /// 默认的代理：系统代理的端口 → 认识的代理软件的 HTTP 端口 → 任一 HTTP 端口 → 任一 SOCKS5 端口
        /// </summary>
        public static ProxyCandidate PickTarget(TerminalProxySnapshot f)
        {
            return f.Candidates.FirstOrDefault(c => c.IsSystemProxy)
                ?? f.Candidates.FirstOrDefault(c => c.Http && c.Owner != null)
                ?? f.Candidates.FirstOrDefault(c => c.Http)
                ?? f.Candidates.FirstOrDefault(c => c.Socks5);
        }

        public static TerminalProxyReport Analyze(TerminalProxySnapshot f, ProxyCandidate target, TerminalProxyBackup backup)
        {
            var report = new TerminalProxyReport { GeneratedAt = f.GeneratedAt, Target = target };
            string owner = target?.Owner ?? f.Clients.FirstOrDefault();

            // 代理本身
            TerminalProxyRow proxyRow = null;
            if (target != null)
            {
                string name = (target.Owner ?? "本机代理") + " · " + target.Protocol;
                long? ms;
                bool tested = f.Tunnels.TryGetValue(target.Endpoint, out ms);
                if (tested && ms.HasValue)
                    proxyRow = new TerminalProxyRow("proxy", name, CheckStatus.Ok, "可用", $"{target.Endpoint}，经代理访问外网 {ms} ms");
                else if (tested)
                    proxyRow = new TerminalProxyRow("proxy", name, CheckStatus.Warning, "访问失败", $"{target.Endpoint} 能连上，但经代理访问外网失败：节点可能不可用，可在代理软件中切换节点");
                else
                    proxyRow = new TerminalProxyRow("proxy", name, CheckStatus.Info, "已发现", target.Endpoint);
                report.Rows.Add(proxyRow);
            }

            // 系统代理
            if (f.SystemProxyEnabled && !string.IsNullOrWhiteSpace(f.SystemProxyServer))
                report.Rows.Add(new TerminalProxyRow("system", "系统代理 · 浏览器", CheckStatus.Ok, "已开启", f.SystemProxyServer + "，Edge、Chrome 等浏览器会走代理"));
            else if (!string.IsNullOrWhiteSpace(f.PacUrl))
                report.Rows.Add(new TerminalProxyRow("system", "系统代理 · 浏览器", CheckStatus.Info, "PAC", f.PacUrl));
            else
                report.Rows.Add(new TerminalProxyRow("system", "系统代理 · 浏览器", CheckStatus.Info, "未开启",
                    target != null ? "代理软件没有设置系统代理（可能是 TUN 模式或手动模式）" : "浏览器等程序直连上网"));

            // 环境变量
            TerminalProxyRow envRow = EnvironmentRow(f, target);
            report.Rows.Add(envRow);

            // 单独设置了代理的工具
            foreach (ToolProxySetting tool in f.Tools.Where(t => t.Installed || t.Values.Count > 0))
                report.Rows.Add(ToolRow(f, tool, target));

            // WinHTTP
            string winHttp = f.WinHttpProxy;
            if (string.IsNullOrWhiteSpace(winHttp))
                report.Rows.Add(new TerminalProxyRow("winhttp", "WinHTTP · 系统服务", CheckStatus.Info, "直连", "Windows 更新等系统服务直连，一般不用改"));
            else if (f.IsReachable(ProxySettings.FirstEndpoint(winHttp)) == false)
                report.Rows.Add(new TerminalProxyRow("winhttp", "WinHTTP · 系统服务", CheckStatus.Warning, "已失效",
                    winHttp + " 无法连接，会影响 Windows 更新；可以管理员身份运行 netsh winhttp reset proxy 恢复直连"));
            else
                report.Rows.Add(new TerminalProxyRow("winhttp", "WinHTTP · 系统服务", CheckStatus.Info, "已设置", winHttp));

            BuildApplyChanges(f, target, report.ApplyChanges);
            BuildClearChanges(f, backup, report.ClearChanges);
            Summarize(f, report, envRow, proxyRow, owner);

            // 链路
            report.Steps.Add(new TerminalProxyStep(owner ?? "代理软件", owner != null || target != null ? CheckStatus.Ok : CheckStatus.Skipped));
            report.Steps.Add(new TerminalProxyStep(target != null ? "端口 " + target.Port : "本机端口",
                target == null ? CheckStatus.Skipped : proxyRow.Status == CheckStatus.Warning ? CheckStatus.Warning : CheckStatus.Ok));
            report.Steps.Add(new TerminalProxyStep("系统代理", f.SystemProxyEnabled ? CheckStatus.Ok : !string.IsNullOrWhiteSpace(f.PacUrl) ? CheckStatus.Info : CheckStatus.Skipped));
            CheckStatus terminal = report.Rows.Where(r => r.Key == "env" || r.Key == "git" || r.Key == "npm" || r.Key == "pip").Select(r => r.Status)
                .DefaultIfEmpty(CheckStatus.Ok).OrderBy(Severity).Last();
            report.Steps.Add(new TerminalProxyStep("命令行", envRow.Status == CheckStatus.Info ? CheckStatus.Skipped : terminal));
            return report;
        }

        private static TerminalProxyRow EnvironmentRow(TerminalProxySnapshot f, ProxyCandidate target)
        {
            const string title = "环境变量 · cmd、PowerShell、curl…";
            string http = f.EffectiveEnvironment(HttpProxy), https = f.EffectiveEnvironment(HttpsProxy), all = f.EffectiveEnvironment(AllProxy);
            string url = https ?? http ?? all;
            string name = https != null ? HttpsProxy : http != null ? HttpProxy : AllProxy;
            if (url == null)
            {
                return target != null
                    ? new TerminalProxyRow("env", title, CheckStatus.Warning, "未设置", "新开的终端不会走代理：git、curl、pip、npm、go 等只认 HTTP_PROXY / HTTPS_PROXY")
                    : new TerminalProxyRow("env", title, CheckStatus.Info, "未设置", "命令行直连上网");
            }

            string endpoint = ProxySettings.UrlEndpoint(url);
            string scope = f.UserEnvironment.ContainsKey(name) && !string.IsNullOrWhiteSpace(f.UserEnvironment[name]) ? "" : "（系统级）";
            if (f.IsReachable(endpoint) == false)
                return new TerminalProxyRow("env", title, CheckStatus.Fail, "已失效", $"{name}{scope} = {url}，但这个端口没有程序在监听：命令行工具会连不上网");
            if (target != null && !SameEndpoint(endpoint, target.Endpoint))
                return new TerminalProxyRow("env", title, CheckStatus.Warning, "不一致", $"{name}{scope} = {url}，与当前代理 {target.Endpoint} 不同");
            if (https == null && http != null && all == null)
                return new TerminalProxyRow("env", title, CheckStatus.Warning, "缺 HTTPS", $"只设置了 HTTP_PROXY = {http}：https 请求（绝大多数）不会走代理");
            string names = https != null && http != null ? "HTTP_PROXY / HTTPS_PROXY" : name;
            return new TerminalProxyRow("env", title, CheckStatus.Ok, "已生效", $"{names}{scope} = {url}");
        }

        private static TerminalProxyRow ToolRow(TerminalProxySnapshot f, ToolProxySetting tool, ProxyCandidate target)
        {
            string key = tool.Tool.ToString().ToLowerInvariant();
            string title = ToolProxySetting.NameOf(tool.Tool);
            if (tool.Values.Count == 0)
                return new TerminalProxyRow(key, title, CheckStatus.Ok, "跟随", "没有单独设置代理，跟随环境变量");

            KeyValuePair<string, string> dead = tool.Values.FirstOrDefault(v => f.IsReachable(ProxySettings.UrlEndpoint(v.Value)) == false);
            if (dead.Key != null)
                return new TerminalProxyRow(key, title, CheckStatus.Fail, "已失效", $"{dead.Key} = {dead.Value}，端口无响应：{title} 会连不上网（它优先于环境变量）");
            KeyValuePair<string, string> other = target == null ? default(KeyValuePair<string, string>)
                : tool.Values.FirstOrDefault(v => !SameEndpoint(ProxySettings.UrlEndpoint(v.Value), target.Endpoint));
            if (other.Key != null)
                return new TerminalProxyRow(key, title, CheckStatus.Warning, "不一致", $"{other.Key} = {other.Value}，优先于环境变量，与当前代理 {target.Endpoint} 不同");
            KeyValuePair<string, string> first = tool.Values.First();
            return new TerminalProxyRow(key, title, CheckStatus.Ok, "已生效", $"{first.Key} = {first.Value}");
        }

        private static void BuildApplyChanges(TerminalProxySnapshot f, ProxyCandidate target, List<ProxyChange> changes)
        {
            if (target == null) return;
            string url = target.Url;
            foreach (string name in ProxyVariables)
            {
                string current = f.EffectiveEnvironment(name);
                // ALL_PROXY 原来没有就不加：HTTP_PROXY / HTTPS_PROXY 已覆盖 http 和 https
                if (name == AllProxy && current == null) continue;
                if (!SameUrl(current, url)) changes.Add(new ProxyChange(ProxyChangeTarget.Environment, name, UserValue(f, name), url));
            }
            if (f.EffectiveEnvironment(NoProxy) == null)
                changes.Add(new ProxyChange(ProxyChangeTarget.Environment, NoProxy, null, DefaultNoProxy));

            // 工具自己的设置优先于环境变量：已设置的改成当前代理，没设置的保持跟随环境变量
            foreach (ToolProxySetting tool in f.Tools)
            {
                if (tool.Tool != ProxyTool.Git && !target.Http) continue; // npm、pip 不支持 SOCKS
                foreach (KeyValuePair<string, string> value in tool.Values)
                {
                    if (!SameUrl(value.Value, url))
                        changes.Add(new ProxyChange((ProxyChangeTarget)((int)tool.Tool + 1), value.Key, value.Value, url));
                }
            }
        }

        private static void BuildClearChanges(TerminalProxySnapshot f, TerminalProxyBackup backup, List<ProxyChange> changes)
        {
            foreach (string name in ProxyVariables)
            {
                string current = UserValue(f, name);
                if (current == null) continue;
                bool ours = backup != null && backup.Wrote(name);
                if (!ours && !IsLoopback(current)) continue;
                // 设置前就有、且不是本机代理的值（如公司代理）恢复回去
                string previous = backup == null ? null : PreviousValue(backup, name);
                string restore = previous != null && !IsLoopback(previous) ? previous : null;
                if (!SameUrl(current, restore)) changes.Add(new ProxyChange(ProxyChangeTarget.Environment, name, current, restore));
            }
            string noProxy = UserValue(f, NoProxy);
            if (noProxy != null && backup != null && backup.Wrote(NoProxy) && PreviousValue(backup, NoProxy) == null && noProxy == DefaultNoProxy)
                changes.Add(new ProxyChange(ProxyChangeTarget.Environment, NoProxy, noProxy, null));

            foreach (ToolProxySetting tool in f.Tools)
            {
                foreach (KeyValuePair<string, string> value in tool.Values.Where(v => IsLoopback(v.Value)))
                    changes.Add(new ProxyChange((ProxyChangeTarget)((int)tool.Tool + 1), value.Key, value.Value, null));
            }
        }

        private static void Summarize(TerminalProxySnapshot f, TerminalProxyReport report, TerminalProxyRow envRow, TerminalProxyRow proxyRow, string owner)
        {
            ProxyCandidate target = report.Target;
            List<TerminalProxyRow> terminalRows = report.Rows.Where(r => r.Key == "env" || r.Key == "git" || r.Key == "npm" || r.Key == "pip").ToList();
            int bad = terminalRows.Count(r => r.Status == CheckStatus.Fail || r.Status == CheckStatus.Warning);
            bool anyFail = terminalRows.Any(r => r.Status == CheckStatus.Fail);

            if (target == null)
            {
                if (envRow.Status == CheckStatus.Fail)
                {
                    report.Overall = CheckStatus.Fail;
                    report.Title = "命令行代理已失效";
                    report.Detail = "环境变量指向的端口没有代理软件在监听，git、pip、curl 会连不上网。点“清除”恢复直连，或先启动代理软件再检测";
                }
                else if (bad > 0)
                {
                    report.Overall = anyFail ? CheckStatus.Fail : CheckStatus.Warning;
                    report.Title = "命令行代理设置有问题";
                    report.Detail = terminalRows.First(r => r.Status == CheckStatus.Fail || r.Status == CheckStatus.Warning).Detail;
                }
                else
                {
                    report.Overall = CheckStatus.Info;
                    report.Title = "没有发现正在运行的代理";
                    report.Detail = "启动 v2rayN、Clash 等代理软件后点“重新检测”。命令行现在直连上网";
                }
                return;
            }

            if (envRow.Status == CheckStatus.Warning && envRow.Badge == "未设置")
            {
                report.Overall = CheckStatus.Warning;
                report.Title = "命令行还没有走代理";
                report.Detail = (owner ?? "代理软件") + (f.SystemProxyEnabled ? " 只设置了系统代理：浏览器能用，但" : " 没有设置命令行代理：")
                    + "cmd、PowerShell、git、curl 只认环境变量。一键设置后，新开的终端即可走代理";
            }
            else if (bad > 0)
            {
                report.Overall = anyFail ? CheckStatus.Fail : CheckStatus.Warning;
                report.Title = anyFail ? "命令行代理设置已失效" : "部分命令行设置与当前代理不一致";
                report.Detail = $"有 {bad} 项没有指向当前代理 {target.Endpoint}，一键设置可全部改正";
            }
            else if (proxyRow != null && proxyRow.Status == CheckStatus.Warning)
            {
                report.Overall = CheckStatus.Warning;
                report.Title = "命令行已走代理，但代理访问外网失败";
                report.Detail = proxyRow.Detail;
            }
            else
            {
                report.Overall = CheckStatus.Ok;
                report.Title = "命令行已走代理";
                long? ms;
                f.Tunnels.TryGetValue(target.Endpoint, out ms);
                report.Detail = $"新开的终端经 {target.Endpoint} 上网" + (ms.HasValue ? $"，访问外网 {ms} ms" : "")
                    + "。已打开的终端需要重开，或粘贴下方命令";
            }
        }

        private static int Severity(CheckStatus status)
        {
            switch (status)
            {
                case CheckStatus.Fail: return 4;
                case CheckStatus.Warning: return 3;
                case CheckStatus.Ok: return 2;
                case CheckStatus.Info: return 1;
                default: return 0;
            }
        }

        private static string UserValue(TerminalProxySnapshot f, string name)
        {
            string value;
            return f.UserEnvironment.TryGetValue(name, out value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
        }

        private static string PreviousValue(TerminalProxyBackup backup, string name)
        {
            foreach (KeyValuePair<string, string> pair in backup.Previous)
            {
                if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)) return string.IsNullOrWhiteSpace(pair.Value) ? null : pair.Value;
            }
            return null;
        }

        /// <summary>
        /// 代理地址是否指向本机（127.x、localhost、::1）：这类设置在代理软件退出后一定会失效
        /// </summary>
        public static bool IsLoopback(string url)
        {
            string endpoint = ProxySettings.UrlEndpoint(url);
            string host;
            int port;
            if (!ProxySettings.TrySplit(endpoint, out host, out port)) return false;
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
            IPAddress address;
            return IPAddress.TryParse(host, out address) && IPAddress.IsLoopback(address);
        }

        public static bool SameEndpoint(string a, string b)
        {
            if (a == null || b == null) return false;
            return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string endpoint)
        {
            return endpoint.Replace("localhost:", "127.0.0.1:");
        }

        public static bool SameUrl(string a, string b)
        {
            if (a == null || b == null) return a == b;
            return string.Equals(a.Trim().TrimEnd('/'), b.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
        }
    }
}
