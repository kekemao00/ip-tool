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
        /// 本机地址不走代理；系统代理有例外列表时再加上它，见 BuildNoProxy
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
            string systemEndpoint = f.SystemProxyEnabled ? SystemProxyEndpoint(f.SystemProxyServer, out _) : null;
            bool systemDead = systemEndpoint != null && (f.IsReachable(systemEndpoint) ?? f.IsReachable(Normalize(systemEndpoint))) == false;
            TerminalProxyRow systemRow;
            if (systemDead)
                systemRow = new TerminalProxyRow("system", "系统代理 · 浏览器", CheckStatus.Fail, "已失效",
                    f.SystemProxyServer + " 没有程序在监听：浏览器会打不开网页。请启动代理软件，或在 Windows 设置 → 网络和 Internet → 代理中关闭“使用代理服务器”");
            else if (f.SystemProxyEnabled && !string.IsNullOrWhiteSpace(f.SystemProxyServer))
                systemRow = new TerminalProxyRow("system", "系统代理 · 浏览器", CheckStatus.Ok, "已开启", f.SystemProxyServer + "，Edge、Chrome 等浏览器会走代理");
            else if (!string.IsNullOrWhiteSpace(f.PacUrl))
                systemRow = new TerminalProxyRow("system", "系统代理 · 浏览器", CheckStatus.Info, "PAC", f.PacUrl);
            else
                systemRow = new TerminalProxyRow("system", "系统代理 · 浏览器", CheckStatus.Info, "未开启",
                    target != null ? "代理软件没有设置系统代理（可能是 TUN 模式或手动模式）" : "浏览器等程序直连上网");
            report.Rows.Add(systemRow);

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

            report.NoProxy = f.EffectiveEnvironment(NoProxy) ?? BuildNoProxy(f.SystemProxyBypass);
            BuildApplyChanges(f, target, report.NoProxy, report.ApplyChanges);
            BuildClearChanges(f, backup, report.ClearChanges);
            report.StaleAfterClear = StaleAfterClear(f, report.ClearChanges);
            Summarize(f, report, envRow, proxyRow, systemRow, owner, systemDead);

            // 链路
            report.Steps.Add(new TerminalProxyStep(owner ?? "代理软件", owner != null || target != null ? CheckStatus.Ok : CheckStatus.Skipped));
            report.Steps.Add(new TerminalProxyStep(target != null ? "端口 " + target.Port : "本机端口",
                target == null ? CheckStatus.Skipped : proxyRow.Status == CheckStatus.Warning ? CheckStatus.Warning : CheckStatus.Ok));
            report.Steps.Add(new TerminalProxyStep("系统代理", systemDead ? CheckStatus.Fail
                : f.SystemProxyEnabled ? CheckStatus.Ok : !string.IsNullOrWhiteSpace(f.PacUrl) ? CheckStatus.Info : CheckStatus.Skipped));
            CheckStatus terminal = TerminalRows(report).Select(r => r.Status).DefaultIfEmpty(CheckStatus.Ok).OrderBy(Severity).Last();
            // 没设环境变量也没有代理时命令行这一环不适用，除非 Git / npm / pip 自己的设置有问题
            report.Steps.Add(new TerminalProxyStep("命令行", envRow.Status == CheckStatus.Info && !IsBad(terminal) ? CheckStatus.Skipped : terminal));
            return report;
        }

        private static TerminalProxyRow EnvironmentRow(TerminalProxySnapshot f, ProxyCandidate target)
        {
            const string title = "环境变量 · git、curl、npm…";
            string http = f.EffectiveEnvironment(HttpProxy), https = f.EffectiveEnvironment(HttpsProxy), all = f.EffectiveEnvironment(AllProxy);
            if (http == null && https == null && all == null)
            {
                return target != null
                    ? new TerminalProxyRow("env", title, CheckStatus.Warning, "未设置", "新开的终端不会走代理：git、curl、npm、go 等只认 HTTP_PROXY / HTTPS_PROXY")
                    : new TerminalProxyRow("env", title, CheckStatus.Info, "未设置", "命令行直连上网");
            }

            // 每个变量都要检查；ALL_PROXY 只在 HTTP_PROXY、HTTPS_PROXY 缺一个时才会被用到
            var set = new List<KeyValuePair<string, string>>();
            if (http != null) set.Add(new KeyValuePair<string, string>(HttpProxy, http));
            if (https != null) set.Add(new KeyValuePair<string, string>(HttpsProxy, https));
            if (all != null && (http == null || https == null)) set.Add(new KeyValuePair<string, string>(AllProxy, all));

            foreach (KeyValuePair<string, string> v in set)
            {
                if (f.IsReachable(ProxySettings.UrlEndpoint(v.Value)) == false)
                    return new TerminalProxyRow("env", title, CheckStatus.Fail, "已失效", $"{v.Key}{Scope(f, v.Key)} = {v.Value}，但这个端口没有程序在监听：命令行工具会连不上网");
            }
            if (target != null)
            {
                foreach (KeyValuePair<string, string> v in set)
                {
                    if (!SameEndpoint(ProxySettings.UrlEndpoint(v.Value), target.Endpoint))
                        return new TerminalProxyRow("env", title, CheckStatus.Warning, "不一致", $"{v.Key}{Scope(f, v.Key)} = {v.Value}，与当前代理 {target.Endpoint} 不同");
                }
            }
            if (https == null && all == null)
                return new TerminalProxyRow("env", title, CheckStatus.Warning, "缺 HTTPS", $"只设置了 HTTP_PROXY = {http}：https 请求（绝大多数）不会走代理");

            string detail = set.All(v => SameUrl(v.Value, set[0].Value))
                ? string.Join(" / ", set.Select(v => v.Key)) + Scope(f, set[0].Key) + " = " + set[0].Value
                : string.Join("，", set.Select(v => $"{v.Key}{Scope(f, v.Key)} = {v.Value}"));
            return new TerminalProxyRow("env", title, CheckStatus.Ok, "已生效", detail);
        }

        private static string Scope(TerminalProxySnapshot f, string name)
        {
            return UserValue(f, name) != null ? "" : "（系统级）";
        }

        private static TerminalProxyRow ToolRow(TerminalProxySnapshot f, ToolProxySetting tool, ProxyCandidate target)
        {
            string key = tool.Tool.ToString().ToLowerInvariant();
            string title = ToolProxySetting.NameOf(tool.Tool);
            List<KeyValuePair<string, string>> values = tool.Values.Where(v => !ToolProxySetting.IsIgnored(tool.Tool, v.Key)).ToList();
            if (values.Count == 0)
            {
                string env = f.EffectiveEnvironment(HttpsProxy) ?? f.EffectiveEnvironment(HttpProxy) ?? f.EffectiveEnvironment(AllProxy);
                if (tool.Tool == ProxyTool.Pip && IsSocks(env))
                    return new TerminalProxyRow(key, title, CheckStatus.Warning, "不支持 SOCKS",
                        "环境变量是 SOCKS 代理，pip 用不了（报 Missing dependencies for SOCKS support）：请改选 HTTP 端口，或在 pip.ini 中单独设置 HTTP 代理");
                if (tool.Values.Count > 0)
                {
                    KeyValuePair<string, string> ignored = tool.Values.First();
                    return new TerminalProxyRow(key, title, CheckStatus.Info, "跟随", $"{ignored.Key} = {ignored.Value}：{title} 不读取这一项，跟随环境变量");
                }
                return new TerminalProxyRow(key, title, CheckStatus.Ok, "跟随",
                    tool.Tool == ProxyTool.Pip ? "没有单独设置代理，跟随环境变量（没有环境变量时用系统代理）" : "没有单独设置代理，跟随环境变量");
            }

            // npm、pip 不支持 SOCKS，一键设置不会把它们改成 SOCKS 代理
            string manual = target != null && !target.Http && tool.Tool != ProxyTool.Git ? $"；{title} 不支持 SOCKS，一键设置不会改它，请改选 HTTP 端口或手动删除这一项" : "";
            KeyValuePair<string, string> dead = values.FirstOrDefault(v => f.IsReachable(ProxySettings.UrlEndpoint(v.Value)) == false);
            if (dead.Key != null)
                return new TerminalProxyRow(key, title, CheckStatus.Fail, "已失效", $"{dead.Key} = {dead.Value}，端口无响应：{title} 会连不上网（它优先于环境变量）{manual}");
            KeyValuePair<string, string> other = target == null ? default(KeyValuePair<string, string>)
                : values.FirstOrDefault(v => !SameEndpoint(ProxySettings.UrlEndpoint(v.Value), target.Endpoint));
            if (other.Key != null)
                return new TerminalProxyRow(key, title, CheckStatus.Warning, "不一致", $"{other.Key} = {other.Value}，优先于环境变量，与当前代理 {target.Endpoint} 不同{manual}");
            KeyValuePair<string, string> first = values.First();
            return new TerminalProxyRow(key, title, CheckStatus.Ok, "已生效", $"{first.Key} = {first.Value}");
        }

        private static void BuildApplyChanges(TerminalProxySnapshot f, ProxyCandidate target, string noProxy, List<ProxyChange> changes)
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
                changes.Add(new ProxyChange(ProxyChangeTarget.Environment, NoProxy, null, noProxy));

            // 工具自己的设置优先于环境变量：已设置的改成当前代理（原来的值会备份，清除时恢复），没设置的保持跟随环境变量
            foreach (ToolProxySetting tool in f.Tools)
            {
                if (tool.Tool != ProxyTool.Git && !target.Http) continue; // npm、pip 不支持 SOCKS
                foreach (KeyValuePair<string, string> value in tool.Values)
                {
                    if (ToolProxySetting.IsIgnored(tool.Tool, value.Key)) continue;
                    if (!SameUrl(value.Value, url))
                        changes.Add(new ProxyChange(TargetOf(tool.Tool), value.Key, value.Value, url));
                }
            }
        }

        /// <summary>
        /// 清除：本程序写入的、或指向本机的设置。本程序改之前有、且不指向本机的值（如公司代理）恢复回去，其余删除
        /// </summary>
        private static void BuildClearChanges(TerminalProxySnapshot f, TerminalProxyBackup backup, List<ProxyChange> changes)
        {
            foreach (string name in ProxyVariables)
                AddClear(changes, backup, ProxyChangeTarget.Environment, name, UserValue(f, name), true);
            AddClear(changes, backup, ProxyChangeTarget.Environment, NoProxy, UserValue(f, NoProxy), false);

            foreach (ToolProxySetting tool in f.Tools)
            {
                foreach (KeyValuePair<string, string> value in tool.Values)
                    AddClear(changes, backup, TargetOf(tool.Tool), value.Key, value.Value, true);
            }
        }

        private static void AddClear(List<ProxyChange> changes, TerminalProxyBackup backup, ProxyChangeTarget target, string key, string current, bool clearLoopback)
        {
            if (current == null) return;
            string backupKey = TerminalProxyBackup.KeyOf(target, key);
            bool ours = backup != null && backup.Wrote(backupKey, current);
            if (!ours && !(clearLoopback && IsLoopback(current))) return;
            string previous = ours ? backup.PreviousOf(backupKey) : null;
            string restore = previous != null && (!clearLoopback || !IsLoopback(previous)) ? previous : null;
            if (!SameUrl(current, restore)) changes.Add(new ProxyChange(target, key, current, restore));
        }

        /// <summary>
        /// 清除之后各代理变量实际生效的值里，仍然指向失效地址的那个；清除只改用户级变量，系统级的和不指向本机的留着
        /// </summary>
        private static string StaleAfterClear(TerminalProxySnapshot f, List<ProxyChange> clear)
        {
            foreach (string name in ProxyVariables)
            {
                ProxyChange change = clear.FirstOrDefault(c => c.Target == ProxyChangeTarget.Environment && string.Equals(c.Key, name, StringComparison.OrdinalIgnoreCase));
                string user = change != null ? change.NewValue : UserValue(f, name);
                string machine;
                string value = user ?? (f.MachineEnvironment.TryGetValue(name, out machine) && !string.IsNullOrWhiteSpace(machine) ? machine.Trim() : null);
                if (value != null && f.IsReachable(ProxySettings.UrlEndpoint(value)) == false)
                    return $"{name}{(user == null ? "（系统级）" : "")} = {value}";
            }
            return null;
        }

        private static void Summarize(TerminalProxySnapshot f, TerminalProxyReport report, TerminalProxyRow envRow, TerminalProxyRow proxyRow,
            TerminalProxyRow systemRow, string owner, bool systemDead)
        {
            ProxyCandidate target = report.Target;
            List<TerminalProxyRow> badRows = TerminalRows(report).Where(r => IsBad(r.Status)).ToList();
            bool anyFail = badRows.Any(r => r.Status == CheckStatus.Fail);

            if (target == null)
            {
                if (envRow.Status == CheckStatus.Fail)
                {
                    report.Overall = CheckStatus.Fail;
                    report.Title = "命令行代理已失效";
                    report.Detail = report.StaleAfterClear == null
                        ? "环境变量指向的端口没有代理软件在监听，git、pip、curl 会连不上网。点“清除”恢复直连，或先启动代理软件再检测"
                        : ManualFix(report.StaleAfterClear);
                }
                else if (badRows.Count > 0)
                {
                    report.Overall = anyFail ? CheckStatus.Fail : CheckStatus.Warning;
                    report.Title = "命令行代理设置有问题";
                    report.Detail = badRows[0].Detail;
                }
                else if (envRow.Status == CheckStatus.Ok)
                {
                    report.Overall = CheckStatus.Info;
                    report.Title = "命令行已设置代理";
                    report.Detail = envRow.Detail + "。这个地址能连上，但没有识别出是哪个代理软件";
                }
                else if (systemDead)
                {
                    report.Overall = CheckStatus.Fail;
                    report.Title = "系统代理已失效";
                    report.Detail = systemRow.Detail;
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
                report.Detail = (owner ?? "代理软件") + (f.SystemProxyEnabled && !systemDead ? " 只设置了系统代理：浏览器能用，但" : " 没有设置命令行代理：")
                    + "git、curl、npm 等命令行工具只认环境变量。一键设置后，新开的终端即可走代理";
            }
            else if (badRows.Count > 0)
            {
                List<TerminalProxyRow> manual = badRows.Where(r => !WillFix(r, report.ApplyChanges)).ToList();
                int fixable = badRows.Count - manual.Count;
                report.Overall = anyFail ? CheckStatus.Fail : CheckStatus.Warning;
                report.Title = anyFail ? "命令行代理设置已失效" : fixable == 0 ? "部分命令行工具需要手动设置" : "部分命令行设置与当前代理不一致";
                if (manual.Count == 0)
                    report.Detail = $"有 {badRows.Count} 项没有指向当前代理 {target.Endpoint}，一键设置可全部改正";
                else
                    report.Detail = (fixable > 0 ? $"一键设置可改正 {fixable} 项；" : "") + manual[0].Title + "：" + manual[0].Detail;
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

        private static string ManualFix(string stale)
        {
            return stale.Contains("（系统级）")
                ? $"{stale} 指向的端口没有程序在监听。系统级变量要以管理员身份在“系统属性 → 环境变量 → 系统变量”中删除，或先启动代理软件再检测"
                : $"{stale} 连不上。“清除”只删除指向本机的设置：如不再使用这个代理，请在“系统属性 → 环境变量”中删除";
        }

        /// <summary>
        /// 一键设置会改到这一行
        /// </summary>
        private static bool WillFix(TerminalProxyRow row, List<ProxyChange> changes)
        {
            switch (row.Key)
            {
                case "env": return changes.Any(c => c.Target == ProxyChangeTarget.Environment && c.Key != NoProxy);
                case "git": return changes.Any(c => c.Target == ProxyChangeTarget.Git);
                case "npm": return changes.Any(c => c.Target == ProxyChangeTarget.Npm);
                case "pip": return changes.Any(c => c.Target == ProxyChangeTarget.Pip);
                default: return false;
            }
        }

        private static IEnumerable<TerminalProxyRow> TerminalRows(TerminalProxyReport report)
        {
            return report.Rows.Where(r => r.Key == "env" || r.Key == "git" || r.Key == "npm" || r.Key == "pip");
        }

        private static bool IsBad(CheckStatus status)
        {
            return status == CheckStatus.Fail || status == CheckStatus.Warning;
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

        private static ProxyChangeTarget TargetOf(ProxyTool tool)
        {
            return (ProxyChangeTarget)((int)tool + 1);
        }

        private static string UserValue(TerminalProxySnapshot f, string name)
        {
            string value;
            return f.UserEnvironment.TryGetValue(name, out value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
        }

        private static bool IsSocks(string url)
        {
            return url != null && url.TrimStart().StartsWith("socks", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 系统代理 ProxyServer 里给 http / https 用的地址。可以是“host:port”，也可以按协议分别设置，如
        /// “http=127.0.0.1:7890;https=127.0.0.1:7890;socks=127.0.0.1:7891”：依次取 https=、http=；只设置了 socks= 时取它，socks 为 true
        /// </summary>
        public static string SystemProxyEndpoint(string server, out bool socks)
        {
            socks = false;
            string https = null, http = null, socksEndpoint = null;
            foreach (string part in (server ?? "").Split(new[] { ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('=');
                string scheme = eq < 0 ? "http" : part.Substring(0, eq).Trim().ToLowerInvariant();
                string endpoint = ProxySettings.FirstEndpoint(eq < 0 ? part : part.Substring(eq + 1));
                if (endpoint == null) continue;
                if (scheme == "https") https = https ?? endpoint;
                else if (scheme == "http") http = http ?? endpoint;
                else if (scheme == "socks") socksEndpoint = socksEndpoint ?? endpoint;
            }
            if (https != null || http != null) return https ?? http;
            socks = socksEndpoint != null;
            return socksEndpoint;
        }

        /// <summary>
        /// 一键设置写入的 NO_PROXY：本机地址，加上系统代理的例外列表（*.corp.com → .corp.com，10.* → 10.0.0.0/8；&lt;local&gt; 等没有对应写法的略过）
        /// </summary>
        public static string BuildNoProxy(string proxyOverride)
        {
            var entries = new List<string>(DefaultNoProxy.Split(','));
            foreach (string item in (proxyOverride ?? "").Split(';'))
            {
                string entry = NoProxyEntry(item.Trim());
                if (entry != null && !entries.Contains(entry, StringComparer.OrdinalIgnoreCase)) entries.Add(entry);
            }
            // v2rayN 等把 172.16.* 到 172.31.* 逐个列出，合并成一项
            var private172 = Enumerable.Range(16, 16).Select(n => $"172.{n}.0.0/16").ToList();
            if (private172.All(entries.Contains))
            {
                entries[entries.IndexOf(private172[0])] = "172.16.0.0/12";
                entries.RemoveAll(private172.Contains);
            }
            return string.Join(",", entries);
        }

        private static string NoProxyEntry(string item)
        {
            if (item.Length == 0 || item[0] == '<') return null;
            if (item.StartsWith("*.", StringComparison.Ordinal)) item = item.Substring(1);
            string rest = item;
            while (rest.EndsWith(".*", StringComparison.Ordinal)) rest = rest.Substring(0, rest.Length - 2);
            if (rest != item)
            {
                // IP 通配写成网段：192.168.* → 192.168.0.0/16
                string[] octets = rest.Split('.');
                byte b;
                if (octets.Length > 3 || !octets.All(o => byte.TryParse(o, out b))) return null;
                return string.Join(".", octets.Concat(Enumerable.Repeat("0", 4 - octets.Length))) + "/" + octets.Length * 8;
            }
            return item.Contains("*") ? null : item;
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
