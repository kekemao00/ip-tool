using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IP_UpdateTest.Core.Diagnosis;
using IP_UpdateTest.Core.TerminalProxy;
using Xunit;

namespace IP_UpdateTest.Tests.Core
{
    public class TerminalProxyTests
    {
        /// <summary>
        /// 典型场景：v2rayN 开着系统代理（HTTP 10809、SOCKS 10808），命令行什么都没设
        /// </summary>
        private static TerminalProxySnapshot V2rayN()
        {
            var f = new TerminalProxySnapshot { GeneratedAt = new DateTime(2026, 10, 16, 9, 0, 0), SystemProxyEnabled = true, SystemProxyServer = "127.0.0.1:10809" };
            f.Clients.Add("v2rayN");
            f.Candidates.Add(new ProxyCandidate { Port = 10809, Http = true, Owner = "v2rayN", IsSystemProxy = true });
            f.Candidates.Add(new ProxyCandidate { Port = 10808, Socks5 = true, Owner = "v2rayN" });
            f.Reachable["127.0.0.1:10809"] = true;
            f.Reachable["127.0.0.1:10808"] = true;
            f.Tunnels["127.0.0.1:10809"] = 230;
            f.Tools.Add(new ToolProxySetting { Tool = ProxyTool.Git, Installed = true });
            f.Tools.Add(new ToolProxySetting { Tool = ProxyTool.Npm, Installed = false });
            f.Tools.Add(new ToolProxySetting { Tool = ProxyTool.Pip, Installed = false });
            return f;
        }

        private static TerminalProxyReport Analyze(TerminalProxySnapshot f, TerminalProxyBackup backup = null)
        {
            return TerminalProxyAnalyzer.Analyze(f, TerminalProxyAnalyzer.PickTarget(f), backup);
        }

        private static TerminalProxyRow Row(TerminalProxyReport report, string key)
        {
            return report.Rows.Single(r => r.Key == key);
        }

        [Fact]
        public void PickTarget_PrefersSystemProxyThenHttp()
        {
            TerminalProxySnapshot f = V2rayN();
            Assert.Equal(10809, TerminalProxyAnalyzer.PickTarget(f).Port);

            f.Candidates.RemoveAt(0);
            f.Candidates.Add(new ProxyCandidate { Port = 7890, Http = true, Socks5 = true });
            Assert.Equal(7890, TerminalProxyAnalyzer.PickTarget(f).Port);
        }

        [Fact]
        public void SystemProxyOnly_SuggestsApply()
        {
            TerminalProxyReport report = Analyze(V2rayN());

            Assert.Equal(CheckStatus.Warning, report.Overall);
            Assert.Equal("命令行还没有走代理", report.Title);
            Assert.Contains("v2rayN 只设置了系统代理", report.Detail);
            Assert.Equal(CheckStatus.Warning, Row(report, "env").Status);
            Assert.Equal(CheckStatus.Ok, Row(report, "git").Status);
            Assert.Equal(CheckStatus.Ok, Row(report, "proxy").Status);
            Assert.DoesNotContain(report.Rows, r => r.Key == "npm");

            // 写 HTTP_PROXY、HTTPS_PROXY 和 NO_PROXY；原来没有 ALL_PROXY 就不加
            Assert.Equal(new[] { "HTTP_PROXY", "HTTPS_PROXY", "NO_PROXY" }, report.ApplyChanges.Select(c => c.Key));
            Assert.All(report.ApplyChanges.Take(2), c => Assert.Equal("http://127.0.0.1:10809", c.NewValue));
            Assert.Empty(report.ClearChanges);
            Assert.Equal(CheckStatus.Warning, report.Steps.Last().Status);
        }

        [Fact]
        public void Configured_IsOk()
        {
            TerminalProxySnapshot f = V2rayN();
            f.UserEnvironment["HTTP_PROXY"] = "http://127.0.0.1:10809";
            f.UserEnvironment["HTTPS_PROXY"] = "http://127.0.0.1:10809";
            f.UserEnvironment["NO_PROXY"] = "localhost";

            TerminalProxyReport report = Analyze(f);

            Assert.Equal(CheckStatus.Ok, report.Overall);
            Assert.Contains("230 ms", report.Detail);
            Assert.Empty(report.ApplyChanges);
            Assert.Equal(new[] { "HTTP_PROXY", "HTTPS_PROXY" }, report.ClearChanges.Select(c => c.Key));
            Assert.All(report.ClearChanges, c => Assert.Null(c.NewValue));
        }

        [Fact]
        public void StaleEnvironment_WithoutProxy_SuggestsClear()
        {
            var f = new TerminalProxySnapshot();
            f.UserEnvironment["HTTPS_PROXY"] = "http://127.0.0.1:7890";
            f.Reachable["127.0.0.1:7890"] = false;

            TerminalProxyReport report = Analyze(f);

            Assert.Null(report.Target);
            Assert.Equal(CheckStatus.Fail, report.Overall);
            Assert.Equal("命令行代理已失效", report.Title);
            Assert.Equal(CheckStatus.Fail, Row(report, "env").Status);
            Assert.Empty(report.ApplyChanges);
            Assert.Single(report.ClearChanges);
        }

        [Fact]
        public void StaleGitProxy_IsFixedByApply()
        {
            TerminalProxySnapshot f = V2rayN();
            f.UserEnvironment["HTTP_PROXY"] = f.UserEnvironment["HTTPS_PROXY"] = "http://127.0.0.1:10809";
            f.UserEnvironment["NO_PROXY"] = "localhost";
            f.Tools[0].Values["http.proxy"] = "http://127.0.0.1:7890";
            f.Reachable["127.0.0.1:7890"] = false;

            TerminalProxyReport report = Analyze(f);

            Assert.Equal(CheckStatus.Fail, Row(report, "git").Status);
            Assert.Equal(CheckStatus.Fail, report.Overall);
            ProxyChange change = Assert.Single(report.ApplyChanges);
            Assert.Equal(ProxyChangeTarget.Git, change.Target);
            Assert.Equal("Git http.proxy = http://127.0.0.1:10809", change.ToString());
            // 清除时删掉指向本机的 Git 代理
            Assert.Contains(report.ClearChanges, c => c.Target == ProxyChangeTarget.Git && c.NewValue == null);
        }

        [Fact]
        public void DifferentPort_IsWarning()
        {
            TerminalProxySnapshot f = V2rayN();
            f.UserEnvironment["HTTPS_PROXY"] = "http://127.0.0.1:10808";

            TerminalProxyRow row = Row(Analyze(f), "env");

            Assert.Equal(CheckStatus.Warning, row.Status);
            Assert.Equal("不一致", row.Badge);
        }

        [Fact]
        public void OnlyHttpProxy_WarnsAboutHttps()
        {
            TerminalProxySnapshot f = V2rayN();
            f.UserEnvironment["HTTP_PROXY"] = "http://127.0.0.1:10809";

            TerminalProxyReport report = Analyze(f);

            Assert.Equal("缺 HTTPS", Row(report, "env").Badge);
            Assert.Contains(report.ApplyChanges, c => c.Key == "HTTPS_PROXY");
        }

        [Fact]
        public void SocksOnly_UsesSocksUrlAndSkipsNpm()
        {
            var f = new TerminalProxySnapshot();
            f.Candidates.Add(new ProxyCandidate { Port = 1080, Socks5 = true });
            var npm = new ToolProxySetting { Tool = ProxyTool.Npm, Installed = true };
            npm.Values["proxy"] = "http://10.0.0.1:3128";
            f.Tools.Add(npm);

            TerminalProxyReport report = Analyze(f);

            Assert.Equal("socks5h://127.0.0.1:1080", report.Target.Url);
            Assert.DoesNotContain(report.ApplyChanges, c => c.Target == ProxyChangeTarget.Npm);
        }

        [Fact]
        public void Clear_RestoresCompanyProxyFromBackup()
        {
            TerminalProxySnapshot f = V2rayN();
            f.UserEnvironment["HTTPS_PROXY"] = "http://127.0.0.1:10809";
            f.UserEnvironment["NO_PROXY"] = TerminalProxyAnalyzer.DefaultNoProxy;
            var backup = new TerminalProxyBackup();
            backup.Record("HTTPS_PROXY", "http://proxy.corp:8080", "http://127.0.0.1:10809");
            backup.Record("NO_PROXY", null, TerminalProxyAnalyzer.DefaultNoProxy);

            TerminalProxyReport report = Analyze(f, backup);

            Assert.Contains(report.ClearChanges, c => c.Key == "HTTPS_PROXY" && c.NewValue == "http://proxy.corp:8080");
            // 恢复公司代理后 NO_PROXY 留着：本机地址不该走公司代理
            Assert.DoesNotContain(report.ClearChanges, c => c.Key == "NO_PROXY");
        }

        [Fact]
        public void Clear_LeavesNonLocalProxyWeDidNotWrite()
        {
            var f = new TerminalProxySnapshot();
            f.UserEnvironment["HTTPS_PROXY"] = "http://proxy.corp:8080";

            Assert.Empty(Analyze(f).ClearChanges);
        }

        [Fact]
        public void MachineLevelVariable_IsEffective()
        {
            TerminalProxySnapshot f = V2rayN();
            f.MachineEnvironment["HTTPS_PROXY"] = "http://127.0.0.1:10809";
            f.MachineEnvironment["HTTP_PROXY"] = "http://127.0.0.1:10809";

            TerminalProxyRow row = Row(Analyze(f), "env");

            Assert.Equal(CheckStatus.Ok, row.Status);
            Assert.Contains("系统级", row.Detail);
        }

        [Fact]
        public void TunnelFailure_IsReported()
        {
            TerminalProxySnapshot f = V2rayN();
            f.Tunnels["127.0.0.1:10809"] = null;
            f.UserEnvironment["HTTP_PROXY"] = f.UserEnvironment["HTTPS_PROXY"] = "http://127.0.0.1:10809";
            f.UserEnvironment["NO_PROXY"] = "localhost";

            TerminalProxyReport report = Analyze(f);

            Assert.Equal(CheckStatus.Warning, Row(report, "proxy").Status);
            Assert.Equal(CheckStatus.Warning, report.Overall);
            Assert.Contains("切换节点", report.Detail);
        }

        [Fact]
        public void NoProxyAnywhere_IsNeutral()
        {
            TerminalProxyReport report = Analyze(new TerminalProxySnapshot());

            Assert.Equal(CheckStatus.Info, report.Overall);
            Assert.Empty(report.ApplyChanges);
            Assert.Empty(report.ClearChanges);
        }

        [Theory]
        [InlineData("http://127.0.0.1:7890", true)]
        [InlineData("socks5://localhost:1080", true)]
        [InlineData("127.0.0.1:10809", true)]
        [InlineData("http://192.168.1.2:7890", false)]
        [InlineData("http://proxy.corp:8080", false)]
        public void IsLoopback(string url, bool expected)
        {
            Assert.Equal(expected, TerminalProxyAnalyzer.IsLoopback(url));
        }

        [Fact]
        public void Follower_FollowsSystemProxy()
        {
            var f = new TerminalProxySnapshot { SystemProxyEnabled = true, SystemProxyServer = "127.0.0.1:7890" };
            List<ProxyChange> on = TerminalProxyFollower.Decide(f, null);
            Assert.Contains(on, c => c.Key == "HTTPS_PROXY" && c.NewValue == "http://127.0.0.1:7890");
            Assert.All(on, c => Assert.Equal(ProxyChangeTarget.Environment, c.Target));

            var off = new TerminalProxySnapshot();
            off.UserEnvironment["HTTPS_PROXY"] = "http://127.0.0.1:7890";
            Assert.Contains(TerminalProxyFollower.Decide(off, null), c => c.Key == "HTTPS_PROXY" && c.NewValue == null);

            // PAC 模式不动
            off.PacUrl = "http://127.0.0.1:10810/pac";
            Assert.Empty(TerminalProxyFollower.Decide(off, null));
        }

        [Fact]
        public void Snippets_ForEachShell()
        {
            const string url = "http://127.0.0.1:10809";
            Assert.Contains("$env:HTTPS_PROXY = 'http://127.0.0.1:10809'", ShellSnippets.Build(ShellKind.PowerShell, url));
            Assert.Contains("set HTTPS_PROXY=http://127.0.0.1:10809", ShellSnippets.Build(ShellKind.Cmd, url));
            Assert.Contains("export http_proxy=http://127.0.0.1:10809", ShellSnippets.Build(ShellKind.Bash, url));
            Assert.Contains("Remove-Item Env:HTTP_PROXY", ShellSnippets.Build(ShellKind.PowerShell, null));
            Assert.Contains("unset http_proxy", ShellSnippets.Build(ShellKind.Bash, null));
            Assert.Equal("Git Bash", ShellSnippets.NameOf(ShellKind.Bash));

            // 同一条 export 里的 $no_proxy 会在赋值前展开，必须分成两条命令
            Assert.Contains("export no_proxy=localhost,127.0.0.1,::1; export NO_PROXY=$no_proxy", ShellSnippets.Build(ShellKind.Bash, url));
        }

        [Fact]
        public void Snippets_QuoteTheUsersNoProxy()
        {
            // 用户已有的 NO_PROXY 可能带空格、分号或 &lt;local&gt;：不加引号 bash 会报错，cmd 会当成重定向
            const string url = "http://127.0.0.1:10809", noProxy = "localhost, 127.0.0.1;<local>";
            Assert.Contains("export no_proxy='localhost, 127.0.0.1;<local>'; export NO_PROXY=$no_proxy", ShellSnippets.Build(ShellKind.Bash, url, noProxy));
            Assert.Contains("set \"NO_PROXY=localhost, 127.0.0.1;<local>\"", ShellSnippets.Build(ShellKind.Cmd, url, noProxy));
            Assert.Contains("$env:NO_PROXY = 'it''s'", ShellSnippets.Build(ShellKind.PowerShell, url, "it's"));
            Assert.Contains("export no_proxy='it'\\''s'", ShellSnippets.Build(ShellKind.Bash, url, "it's"));
            Assert.Contains("set NO_PROXY=localhost,127.0.0.1", ShellSnippets.Build(ShellKind.Cmd, url, "localhost,127.0.0.1"));
        }

        [Fact]
        public void Snippets_SetWhatTheClearLeaves()
        {
            // 清除恢复了公司代理、删掉了 NO_PROXY：已打开的终端照此设置
            var values = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("HTTPS_PROXY", "http://proxy.corp:8080"),
                new KeyValuePair<string, string>("HTTP_PROXY", null),
                new KeyValuePair<string, string>("NO_PROXY", null)
            };
            string ps = ShellSnippets.BuildVariables(ShellKind.PowerShell, values);
            Assert.Contains("$env:HTTPS_PROXY = 'http://proxy.corp:8080'", ps);
            Assert.Contains("Remove-Item Env:HTTP_PROXY, Env:NO_PROXY -ErrorAction SilentlyContinue", ps);
            Assert.DoesNotContain("Env:HTTPS_PROXY,", ps);
            Assert.Contains("set HTTP_PROXY=", ShellSnippets.BuildVariables(ShellKind.Cmd, values));
            string bash = ShellSnippets.BuildVariables(ShellKind.Bash, values);
            Assert.Contains("export https_proxy=http://proxy.corp:8080 HTTPS_PROXY=http://proxy.corp:8080", bash);
            Assert.Contains("unset http_proxy no_proxy HTTP_PROXY NO_PROXY", bash);
        }

        [Fact]
        public void Snippets_WrapLongLines()
        {
            // v2rayN 的例外列表转成 NO_PROXY 后一行放不下：折行显示，尽量在逗号后断开，续行缩进
            string v2rayN = "localhost;127.*;10.*;" + string.Join(";", Enumerable.Range(16, 16).Select(n => $"172.{n}.*")) + ";192.168.*;<local>";
            string text = ShellSnippets.Build(ShellKind.Bash, "http://127.0.0.1:10809", TerminalProxyAnalyzer.BuildNoProxy(v2rayN));
            string longest = text.Split('\n').Select(l => l.TrimEnd('\r')).OrderByDescending(l => l.Length).First();
            Assert.True(longest.Length > 81);

            List<string> lines = ShellSnippets.Wrap(longest, 81);
            Assert.True(lines.Count > 1);
            Assert.All(lines, l => Assert.True(l.Length <= 81, l));
            Assert.All(lines.Skip(1), l => Assert.StartsWith("  ", l));
            Assert.EndsWith(",", lines[0]);
            Assert.Equal(longest.Replace(" ", ""), string.Concat(lines).Replace(" ", ""));

            Assert.Equal(new[] { "short" }, ShellSnippets.Wrap("short", 81));
            Assert.Equal(new[] { "" }, ShellSnippets.Wrap("", 81));
            // 没有可断开的地方时按列硬断
            Assert.Equal(new[] { "abcdefghij", "  klmnopqr", "  st" }, ShellSnippets.Wrap("abcdefghijklmnopqrst", 10));
        }

        [Fact]
        public void ConfigLines_ReadsAndUpdatesOnlyProxyKeys()
        {
            string npmrc = "registry=https://registry.npmmirror.com/\r\n; comment\r\nproxy=http://127.0.0.1:7890\r\nhttps-proxy = \"http://127.0.0.1:7890\"\r\n";
            Dictionary<string, string> values = ConfigLines.Read(npmrc, new[] { "proxy", "https-proxy" });
            Assert.Equal("http://127.0.0.1:7890", values["proxy"]);
            Assert.Equal("http://127.0.0.1:7890", values["https-proxy"]);

            string updated = ConfigLines.Update(npmrc, "https-proxy", "http://127.0.0.1:10809");
            Assert.Contains("https-proxy = http://127.0.0.1:10809\r\n", updated);
            Assert.StartsWith("registry=https://registry.npmmirror.com/\r\n; comment\r\n", updated);

            string removed = ConfigLines.Update(updated, "proxy", null);
            Assert.DoesNotContain("\nproxy=", removed);
            Assert.Contains("https-proxy", removed);

            Assert.Empty(ConfigLines.Read("[global]\nindex-url = https://pypi.org/simple\n", new[] { "proxy" }));

            // npm config set proxy null：npm 关闭代理的写法，不是代理地址；后面的 null 也会盖掉前面的地址
            Assert.Empty(ConfigLines.Read("proxy=http://127.0.0.1:7890\nproxy=null\nhttps-proxy=false\n", new[] { "proxy", "https-proxy" }));
        }

        [Fact]
        public void ConfigFile_KeepsItsEncoding()
        {
            Encoding encoding;
            byte[] bom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("proxy=http://127.0.0.1:7890")).ToArray();
            Assert.Equal("proxy=http://127.0.0.1:7890", TerminalProxyConfigurator.ReadText(bom, out encoding));
            Assert.Equal(3, encoding.GetPreamble().Length);

            Assert.Equal("cache-dir = D:\\软件", TerminalProxyConfigurator.ReadText(Encoding.UTF8.GetBytes("cache-dir = D:\\软件"), out encoding));
            Assert.Empty(encoding.GetPreamble());

            // 不是合法 UTF-8（如 GBK 写的“软件”）时按系统 ANSI 代码页读写，不能当 UTF-8 改坏
            TerminalProxyConfigurator.ReadText(new byte[] { 0x44, 0x3A, 0x5C, 0xC8, 0xED, 0xBC, 0xFE }, out encoding);
            Assert.Equal(Encoding.Default.CodePage, encoding.CodePage);
        }

        [Fact]
        public void PortProbe_RecognizesProtocols()
        {
            Assert.True(ProxyPortProbe.IsHttpResponse(Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection established")));
            Assert.True(ProxyPortProbe.IsHttpResponse(Encoding.ASCII.GetBytes("HTTP/1.1 407 Proxy Authentication Required")));
            Assert.True(ProxyPortProbe.IsHttpResponse(Encoding.ASCII.GetBytes("HTTP/1.0 502 Bad Gateway")));
            // tinyproxy 连不上目标回 500，squid 的访问控制回 403
            Assert.True(ProxyPortProbe.IsHttpResponse(Encoding.ASCII.GetBytes("HTTP/1.0 500 Unable to connect")));
            Assert.True(ProxyPortProbe.IsHttpResponse(Encoding.ASCII.GetBytes("HTTP/1.1 403 Forbidden")));
            // 网页界面、控制接口（v2rayA 2017、Clash 9090）不是代理
            Assert.False(ProxyPortProbe.IsHttpResponse(Encoding.ASCII.GetBytes("HTTP/1.1 404 Not Found")));
            Assert.False(ProxyPortProbe.IsHttpResponse(Encoding.ASCII.GetBytes("HTTP/1.1 405 Method Not Allowed")));
            Assert.False(ProxyPortProbe.IsHttpResponse(Encoding.ASCII.GetBytes("HTTP/1.1 401 Unauthorized")));
            Assert.False(ProxyPortProbe.IsHttpResponse(Encoding.ASCII.GetBytes("HTTP/1.1 400 Bad Request")));
            Assert.False(ProxyPortProbe.IsHttpResponse(Encoding.ASCII.GetBytes("HTTP/")));
            Assert.False(ProxyPortProbe.IsHttpResponse(new byte[] { 5, 0 }));
            Assert.True(ProxyPortProbe.IsSocks5Greeting(new byte[] { 5, 0 }));
            Assert.False(ProxyPortProbe.IsSocks5Greeting(new byte[] { 5, 0xFF }));
            // 端口在表中是网络字节序：10809 = 0x2A39
            Assert.Equal(10809, ProxyPortProbe.NetworkPort(0x392A));
        }

        [Fact]
        public void Follower_FirstReadingIsOnlyABaseline()
        {
            // 启动时 / 刚开启跟随时系统代理关着（TUN 或手动模式），不能清掉刚一键设置好的命令行代理
            Assert.False(TerminalProxyFollower.ShouldSync(null, "off", false));
            Assert.False(TerminalProxyFollower.ShouldSync(null, "pac", false));
            // 开着则立即同步，换了端口也能跟上
            Assert.True(TerminalProxyFollower.ShouldSync(null, "on:127.0.0.1:7890", true));
            // 之后真正的开关变化照常同步
            Assert.True(TerminalProxyFollower.ShouldSync("on:127.0.0.1:7890", "off", false));
            Assert.True(TerminalProxyFollower.ShouldSync("off", "on:127.0.0.1:7890", true));
            Assert.False(TerminalProxyFollower.ShouldSync("off", "off", false));
        }

        [Fact]
        public void Follower_MessageNamesTheProxyNotNoProxy()
        {
            var onlyNoProxy = new List<ProxyChange> { new ProxyChange(ProxyChangeTarget.Environment, "NO_PROXY", null, TerminalProxyAnalyzer.DefaultNoProxy) };
            Assert.DoesNotContain("localhost", TerminalProxyFollower.Message(onlyNoProxy, true));

            var both = new List<ProxyChange>
            {
                new ProxyChange(ProxyChangeTarget.Environment, "NO_PROXY", null, TerminalProxyAnalyzer.DefaultNoProxy),
                new ProxyChange(ProxyChangeTarget.Environment, "HTTPS_PROXY", null, "http://127.0.0.1:7890")
            };
            Assert.EndsWith("http://127.0.0.1:7890", TerminalProxyFollower.Message(both, true));
        }

        [Fact]
        public void Follower_ReadsPerProtocolSystemProxy()
        {
            // 只填了 socks= 时不能把 SOCKS 端口当 HTTP 代理写进去
            var socks = new TerminalProxySnapshot { SystemProxyEnabled = true, SystemProxyServer = "socks=127.0.0.1:10808" };
            Assert.Contains(TerminalProxyFollower.Decide(socks, null), c => c.Key == "HTTPS_PROXY" && c.NewValue == "socks5h://127.0.0.1:10808");

            var mixed = new TerminalProxySnapshot { SystemProxyEnabled = true, SystemProxyServer = "socks=127.0.0.1:7891;http=127.0.0.1:7890" };
            Assert.Contains(TerminalProxyFollower.Decide(mixed, null), c => c.Key == "HTTPS_PROXY" && c.NewValue == "http://127.0.0.1:7890");

            bool isSocks;
            Assert.Equal("127.0.0.1:7892", TerminalProxyAnalyzer.SystemProxyEndpoint("http=127.0.0.1:7890;https=127.0.0.1:7892", out isSocks));
            Assert.False(isSocks);
        }

        [Fact]
        public void Follower_DoesNotShadowMachineNoProxy()
        {
            var f = new TerminalProxySnapshot { SystemProxyEnabled = true, SystemProxyServer = "127.0.0.1:7890" };
            f.MachineEnvironment["NO_PROXY"] = ".corp.example.com,10.0.0.0/8";
            Assert.DoesNotContain(TerminalProxyFollower.Decide(f, null), c => c.Key == "NO_PROXY");
        }

        [Fact]
        public void ToolValues_AreBackedUpAndRestored()
        {
            // Git 原来走公司代理，一键设置改成本机代理
            TerminalProxySnapshot f = V2rayN();
            f.UserEnvironment["HTTP_PROXY"] = f.UserEnvironment["HTTPS_PROXY"] = "http://127.0.0.1:10809";
            f.UserEnvironment["NO_PROXY"] = "localhost";
            f.Tools[0].Values["http.proxy"] = "http://proxy.corp:8080";
            ProxyChange apply = Assert.Single(Analyze(f).ApplyChanges);

            var backup = new TerminalProxyBackup();
            backup.Record(TerminalProxyBackup.KeyOf(apply.Target, apply.Key), apply.OldValue, apply.NewValue);
            f.Tools[0].Values["http.proxy"] = apply.NewValue;

            // 清除时恢复成公司代理，而不是删掉
            ProxyChange restore = Analyze(f, backup).ClearChanges.Single(c => c.Target == ProxyChangeTarget.Git);
            Assert.Equal("http://proxy.corp:8080", restore.NewValue);
        }

        [Fact]
        public void Backup_IgnoresValuesTheUserChangedLater()
        {
            // 一键设置过，之后用户自己删掉、又设成了公司代理：清除不能动它
            var backup = new TerminalProxyBackup();
            backup.Record("HTTPS_PROXY", null, "http://127.0.0.1:10809");
            var f = new TerminalProxySnapshot();
            f.UserEnvironment["HTTPS_PROXY"] = "http://proxy.corp:8080";

            Assert.Empty(Analyze(f, backup).ClearChanges);
            Assert.Empty(TerminalProxyFollower.Decide(f, backup));

            // 用户改过之后再一键设置：记下新的原值
            backup.Record("HTTPS_PROXY", "http://proxy.corp:8080", "http://127.0.0.1:7890");
            Assert.Equal("http://proxy.corp:8080", backup.PreviousOf("HTTPS_PROXY"));
            // 本程序自己换端口：原值不变
            backup.Record("HTTPS_PROXY", "http://127.0.0.1:7890", "http://127.0.0.1:10809");
            Assert.Equal("http://proxy.corp:8080", backup.PreviousOf("HTTPS_PROXY"));
        }

        [Fact]
        public void EveryProxyVariableIsChecked()
        {
            // HTTPS_PROXY 正常，HTTP_PROXY 还指向已关掉的 Clash
            TerminalProxySnapshot f = V2rayN();
            f.UserEnvironment["HTTPS_PROXY"] = "http://127.0.0.1:10809";
            f.UserEnvironment["HTTP_PROXY"] = "http://127.0.0.1:7890";
            f.Reachable["127.0.0.1:7890"] = false;

            TerminalProxyReport report = Analyze(f);

            TerminalProxyRow env = Row(report, "env");
            Assert.Equal(CheckStatus.Fail, env.Status);
            Assert.Contains("HTTP_PROXY = http://127.0.0.1:7890", env.Detail);
            Assert.Equal(CheckStatus.Fail, report.Overall);
            Assert.Contains(report.ApplyChanges, c => c.Key == "HTTP_PROXY");
        }

        [Fact]
        public void GitHttpsProxyIsIgnoredButUrlScopedProxyCounts()
        {
            // Git 不读 https.proxy：失效的 https.proxy 不影响 Git
            TerminalProxySnapshot f = V2rayN();
            f.UserEnvironment["HTTP_PROXY"] = f.UserEnvironment["HTTPS_PROXY"] = "http://127.0.0.1:10809";
            f.UserEnvironment["NO_PROXY"] = "localhost";
            f.Tools[0].Values["https.proxy"] = "http://127.0.0.1:1080";
            f.Reachable["127.0.0.1:1080"] = false;

            TerminalProxyReport report = Analyze(f);
            Assert.Equal(CheckStatus.Info, Row(report, "git").Status);
            Assert.Equal(CheckStatus.Ok, report.Overall);
            Assert.Empty(report.ApplyChanges);
            // 清除时顺手删掉
            Assert.Contains(report.ClearChanges, c => c.Key == "https.proxy");

            // 只对 GitHub 生效的代理指向失效端口：git clone github 会失败
            f.Tools[0].Values["http.https://github.com.proxy"] = "socks5://127.0.0.1:1080";
            report = Analyze(f);
            Assert.Equal(CheckStatus.Fail, Row(report, "git").Status);
            Assert.Contains(report.ApplyChanges, c => c.Key == "http.https://github.com.proxy" && c.NewValue == "http://127.0.0.1:10809");
        }

        [Fact]
        public void MachineLevelStaleVariable_ExplainsManualFix()
        {
            var f = new TerminalProxySnapshot();
            f.MachineEnvironment["HTTPS_PROXY"] = "http://127.0.0.1:7890";
            f.Reachable["127.0.0.1:7890"] = false;

            TerminalProxyReport report = Analyze(f);

            Assert.Equal(CheckStatus.Fail, report.Overall);
            Assert.Empty(report.ClearChanges);
            Assert.Contains("系统级", report.StaleAfterClear);
            Assert.Contains("管理员", report.Detail);
            Assert.DoesNotContain("点“清除”", report.Detail);

            // 用户级和系统级都有：清除用户级之后系统级的仍会生效
            f.UserEnvironment["HTTPS_PROXY"] = "http://127.0.0.1:7890";
            report = Analyze(f);
            Assert.Single(report.ClearChanges);
            Assert.NotNull(report.StaleAfterClear);
        }

        [Fact]
        public void DeadSystemProxy_IsFailure()
        {
            var f = new TerminalProxySnapshot { SystemProxyEnabled = true, SystemProxyServer = "127.0.0.1:10809" };
            f.Reachable["127.0.0.1:10809"] = false;

            TerminalProxyReport report = Analyze(f);

            Assert.Equal(CheckStatus.Fail, Row(report, "system").Status);
            Assert.Equal(CheckStatus.Fail, report.Steps[2].Status);
            Assert.Equal("系统代理已失效", report.Title);
        }

        [Fact]
        public void SocksTarget_WarnsAboutPip()
        {
            var f = new TerminalProxySnapshot();
            f.Candidates.Add(new ProxyCandidate { Port = 1080, Socks5 = true });
            f.UserEnvironment["HTTP_PROXY"] = f.UserEnvironment["HTTPS_PROXY"] = "socks5h://127.0.0.1:1080";
            f.UserEnvironment["NO_PROXY"] = "localhost";
            f.Tools.Add(new ToolProxySetting { Tool = ProxyTool.Pip, Installed = true });

            TerminalProxyReport report = Analyze(f);

            Assert.Equal(CheckStatus.Warning, Row(report, "pip").Status);
            Assert.Empty(report.ApplyChanges);
            Assert.Equal("部分命令行工具需要手动设置", report.Title);
            Assert.DoesNotContain("可全部改正", report.Detail);
        }

        [Fact]
        public void NoProxy_IncludesSystemBypassList()
        {
            string v2rayN = "localhost;127.*;10.*;" + string.Join(";", Enumerable.Range(16, 16).Select(n => $"172.{n}.*")) + ";192.168.*;*.corp.example.com;<local>";
            Assert.Equal("localhost,127.0.0.1,::1,127.0.0.0/8,10.0.0.0/8,172.16.0.0/12,192.168.0.0/16,.corp.example.com", TerminalProxyAnalyzer.BuildNoProxy(v2rayN));
            Assert.Equal(TerminalProxyAnalyzer.DefaultNoProxy, TerminalProxyAnalyzer.BuildNoProxy(null));
            Assert.Equal(TerminalProxyAnalyzer.DefaultNoProxy + ",10.1.2.0/24,intranet", TerminalProxyAnalyzer.BuildNoProxy("10.1.2.*;*corp*;intranet"));

            TerminalProxySnapshot f = V2rayN();
            f.SystemProxyBypass = "localhost;192.168.*";
            Assert.Contains(Analyze(f).ApplyChanges, c => c.Key == "NO_PROXY" && c.NewValue == "localhost,127.0.0.1,::1,192.168.0.0/16");
        }

        [Fact]
        public void TerminalStep_ShowsToolFailureWithoutEnvironment()
        {
            var f = new TerminalProxySnapshot();
            f.Tools.Add(new ToolProxySetting { Tool = ProxyTool.Git, Installed = true });
            f.Tools[0].Values["http.proxy"] = "http://127.0.0.1:7890";
            f.Reachable["127.0.0.1:7890"] = false;

            TerminalProxyReport report = Analyze(f);

            Assert.Equal(CheckStatus.Fail, report.Overall);
            Assert.Equal(CheckStatus.Fail, report.Steps.Last().Status);
        }

        [Fact]
        public void Backup_FailedChangeIsPutBack()
        {
            // 第一次一键设置把 Git 的公司代理改成 7890；第二次改 10809 时 git config 失败，备份要放回去
            const string key = "Git:http.proxy", corp = "http://proxy.corp:8080";
            var backup = new TerminalProxyBackup();
            backup.Record(key, corp, "http://127.0.0.1:7890");
            TerminalProxyBackup.Entry saved = backup.Snapshot(key);
            backup.Record(key, "http://127.0.0.1:7890", "http://127.0.0.1:10809");
            backup.Restore(key, saved);

            Assert.True(backup.Wrote(key, "http://127.0.0.1:7890"));
            var f = new TerminalProxySnapshot();
            f.Tools.Add(new ToolProxySetting { Tool = ProxyTool.Git, Installed = true });
            f.Tools[0].Values["http.proxy"] = "http://127.0.0.1:7890";
            ProxyChange clear = TerminalProxyAnalyzer.Analyze(f, null, backup).ClearChanges.Single(c => c.Target == ProxyChangeTarget.Git);
            Assert.Equal(corp, clear.NewValue);

            // 原来没有的项放回去就是没有
            backup.Restore("HTTPS_PROXY", backup.Snapshot("HTTPS_PROXY"));
            Assert.Null(backup.PreviousOf("HTTPS_PROXY"));
            Assert.False(backup.Wrote("HTTPS_PROXY", "http://127.0.0.1:10809"));
        }

        [Fact]
        public void Backup_KeepsFollowState()
        {
            var backup = new TerminalProxyBackup { FollowState = "on:127.0.0.1:10809" };
            Assert.False(backup.IsEmpty);
            TerminalProxyBackup read = IP_UpdateTest.Core.JsonFile.Deserialize<TerminalProxyBackup>(IP_UpdateTest.Core.JsonFile.Serialize(backup));
            Assert.Equal("on:127.0.0.1:10809", read.FollowState);
            // 上次运行时同步到“开”、启动时已关：程序没运行时系统代理关了，要清除
            Assert.True(TerminalProxyFollower.ShouldSync(read.FollowState, "off", false));
            // 旧格式没有这一项
            Assert.Null(IP_UpdateTest.Core.JsonFile.Deserialize<TerminalProxyBackup>("{}").FollowState);
            Assert.True(new TerminalProxyBackup().IsEmpty);
        }

        [Fact]
        public void Follower_MovesToolProxiesItWrote()
        {
            // 一键设置把 Git 的 http.proxy 改成了 7890；系统代理换到 7897 或关掉时要跟着改，不然 Git 停在失效的端口上
            var backup = new TerminalProxyBackup();
            backup.Record("Git:http.proxy", null, "http://127.0.0.1:7890");
            var git = new ToolProxySetting { Tool = ProxyTool.Git, Installed = true };
            git.Values["http.proxy"] = "http://127.0.0.1:7890";
            var npm = new ToolProxySetting { Tool = ProxyTool.Npm, Installed = true };
            npm.Values["proxy"] = "http://127.0.0.1:3128"; // 用户自己设的，不动

            var on = new TerminalProxySnapshot { SystemProxyEnabled = true, SystemProxyServer = "127.0.0.1:7897" };
            on.Tools.Add(git);
            on.Tools.Add(npm);
            List<ProxyChange> changes = TerminalProxyFollower.Decide(on, backup);
            Assert.Contains(changes, c => c.Target == ProxyChangeTarget.Git && c.NewValue == "http://127.0.0.1:7897");
            Assert.DoesNotContain(changes, c => c.Target == ProxyChangeTarget.Npm);

            var off = new TerminalProxySnapshot();
            off.Tools.Add(git);
            off.Tools.Add(npm);
            changes = TerminalProxyFollower.Decide(off, backup);
            Assert.Contains(changes, c => c.Target == ProxyChangeTarget.Git && c.NewValue == null);
            Assert.DoesNotContain(changes, c => c.Target == ProxyChangeTarget.Npm);
        }

        [Fact]
        public void Follower_Ipv6SystemProxy_WritesValidUrl()
        {
            var f = new TerminalProxySnapshot { SystemProxyEnabled = true, SystemProxyServer = "[::1]:7890" };
            Assert.Contains(TerminalProxyFollower.Decide(f, null), c => c.Key == "HTTPS_PROXY" && c.NewValue == "http://[::1]:7890");
            Assert.Equal("[::1]:7890", ProxyCandidate.EndpointOf("::1", 7890));
            Assert.Equal("127.0.0.1:7890", ProxyCandidate.EndpointOf("127.0.0.1", 7890));
        }

        [Fact]
        public void Follower_MessageWhenRestoring()
        {
            var restored = new List<ProxyChange> { new ProxyChange(ProxyChangeTarget.Environment, "HTTPS_PROXY", "http://127.0.0.1:7890", "http://proxy.corp:8080") };
            Assert.Equal("系统代理已关闭，命令行代理已恢复为 http://proxy.corp:8080", TerminalProxyFollower.Message(restored, false));
            var cleared = new List<ProxyChange> { new ProxyChange(ProxyChangeTarget.Environment, "HTTPS_PROXY", "http://127.0.0.1:7890", null) };
            Assert.Equal("系统代理已关闭，命令行代理已随之清除", TerminalProxyFollower.Message(cleared, false));
        }

        [Fact]
        public void Clear_KeepsNoProxyWhileAnotherProxyStays()
        {
            // 一键设置写过 NO_PROXY，之后用户自己把 HTTP(S)_PROXY 改成了公司代理：清除不能删 NO_PROXY，不然本机和内网也走公司代理
            var backup = new TerminalProxyBackup();
            backup.Record("HTTPS_PROXY", null, "http://127.0.0.1:10809");
            backup.Record("NO_PROXY", null, TerminalProxyAnalyzer.DefaultNoProxy);
            var f = new TerminalProxySnapshot();
            f.UserEnvironment["HTTP_PROXY"] = f.UserEnvironment["HTTPS_PROXY"] = "http://proxy.corp:8080";
            f.UserEnvironment["NO_PROXY"] = TerminalProxyAnalyzer.DefaultNoProxy;

            TerminalProxyReport report = Analyze(f, backup);
            Assert.Empty(report.ClearChanges);
            Assert.Empty(TerminalProxyFollower.Decide(f, backup));

            // 代理变量都会删掉时 NO_PROXY 才一起删
            f.UserEnvironment["HTTP_PROXY"] = f.UserEnvironment["HTTPS_PROXY"] = "http://127.0.0.1:10809";
            report = Analyze(f, backup);
            Assert.Contains(report.ClearChanges, c => c.Key == "NO_PROXY" && c.NewValue == null);
            Assert.Null(report.ProxyAfterClear);
        }

        [Fact]
        public void Clear_ThatRestoresCompanyProxy_SaysSo()
        {
            var backup = new TerminalProxyBackup();
            backup.Record("HTTPS_PROXY", "http://proxy.corp:8080", "http://127.0.0.1:10809");
            var f = new TerminalProxySnapshot();
            f.UserEnvironment["HTTPS_PROXY"] = "http://127.0.0.1:10809";
            f.Reachable["127.0.0.1:10809"] = false;

            TerminalProxyReport report = Analyze(f, backup);

            Assert.Equal("命令行代理已失效", report.Title);
            Assert.DoesNotContain("恢复直连", report.Detail);
            Assert.Contains("http://proxy.corp:8080", report.Detail);
            Assert.Equal("http://proxy.corp:8080", report.ProxyAfterClear);
            KeyValuePair<string, string> v = Assert.Single(report.ClearedVariables);
            Assert.Equal("HTTPS_PROXY", v.Key);
            Assert.Equal("http://proxy.corp:8080", v.Value);
        }

        [Fact]
        public void Clear_OnlyToolSettings_NeedsNoSnippet()
        {
            // 环境变量里的公司代理能用，只有 Git 指向已关掉的本机代理：清除只删 Git 这一项
            var f = new TerminalProxySnapshot();
            f.UserEnvironment["HTTP_PROXY"] = f.UserEnvironment["HTTPS_PROXY"] = "http://proxy.corp:8080";
            f.Reachable["proxy.corp:8080"] = true;
            f.Tools.Add(new ToolProxySetting { Tool = ProxyTool.Git, Installed = true });
            f.Tools[0].Values["http.proxy"] = "http://127.0.0.1:7890";
            f.Reachable["127.0.0.1:7890"] = false;

            TerminalProxyReport report = Analyze(f);

            ProxyChange change = Assert.Single(report.ClearChanges);
            Assert.Equal(ProxyChangeTarget.Git, change.Target);
            Assert.Empty(report.ClearedVariables);
            Assert.Equal("http://proxy.corp:8080", report.ProxyAfterClear);
        }

        [Fact]
        public void Clear_MachineValueTakesOver()
        {
            // 用户级的删掉后系统级的生效：已打开的终端要设成系统级的值，而不是删掉
            var f = new TerminalProxySnapshot();
            f.UserEnvironment["HTTPS_PROXY"] = "http://127.0.0.1:7890";
            f.MachineEnvironment["HTTPS_PROXY"] = "http://proxy.corp:8080";
            f.Reachable["127.0.0.1:7890"] = false;
            f.Reachable["proxy.corp:8080"] = true;

            TerminalProxyReport report = Analyze(f);

            Assert.Null(report.StaleAfterClear);
            KeyValuePair<string, string> v = Assert.Single(report.ClearedVariables);
            Assert.Equal("http://proxy.corp:8080", v.Value);
            Assert.Contains("改经 http://proxy.corp:8080", report.Detail);
        }

        [Fact]
        public void Pip_SocksEnvironmentIsFixedByApplyWithHttpTarget()
        {
            // Clash 混合端口：环境变量写成了 socks5://，一键设置会改成 http://，pip 也就好了
            var f = new TerminalProxySnapshot { SystemProxyEnabled = true, SystemProxyServer = "127.0.0.1:7890" };
            f.Candidates.Add(new ProxyCandidate { Port = 7890, Http = true, Socks5 = true, Owner = "Clash Verge", IsSystemProxy = true });
            f.Reachable["127.0.0.1:7890"] = true;
            f.UserEnvironment["HTTP_PROXY"] = f.UserEnvironment["HTTPS_PROXY"] = "socks5://127.0.0.1:7890";
            f.UserEnvironment["NO_PROXY"] = "localhost";
            f.Tools.Add(new ToolProxySetting { Tool = ProxyTool.Pip, Installed = true });

            TerminalProxyReport report = Analyze(f);

            TerminalProxyRow pip = Row(report, "pip");
            Assert.Equal("不支持 SOCKS", pip.Badge);
            Assert.DoesNotContain("改选 HTTP 端口", pip.Detail);
            Assert.Contains("一键设置", pip.Detail);
            Assert.NotEqual("部分命令行工具需要手动设置", report.Title);
            Assert.Contains("一键设置可全部改正", report.Detail);
        }

        [Fact]
        public void Pip_SocksInPipIniIsWarned()
        {
            TerminalProxySnapshot f = V2rayN();
            f.UserEnvironment["HTTP_PROXY"] = f.UserEnvironment["HTTPS_PROXY"] = "http://127.0.0.1:10809";
            f.UserEnvironment["NO_PROXY"] = "localhost";
            f.Tools[2].Installed = true;
            f.Tools[2].Values["proxy"] = "socks5://127.0.0.1:10808";

            TerminalProxyReport report = Analyze(f);

            Assert.Equal("不支持 SOCKS", Row(report, "pip").Badge);
            Assert.NotEqual(CheckStatus.Ok, report.Overall);
            Assert.Contains(report.ApplyChanges, c => c.Target == ProxyChangeTarget.Pip && c.NewValue == "http://127.0.0.1:10809");
        }

        [Fact]
        public void SocksOnlyTarget_WarnsAheadAboutPip()
        {
            var f = new TerminalProxySnapshot();
            f.Candidates.Add(new ProxyCandidate { Port = 1080, Socks5 = true, Owner = "Shadowsocks" });
            f.Tools.Add(new ToolProxySetting { Tool = ProxyTool.Pip, Installed = true });

            TerminalProxyReport report = Analyze(f);

            Assert.Equal("命令行还没有走代理", report.Title);
            Assert.Contains("pip 用不了", report.Detail);
        }

        [Fact]
        public void UnsetEnvironment_WithManualToolProblem_IsNotPromisedAsFixed()
        {
            // 只有 SOCKS 端口、环境变量没设、npm 指向已关掉的端口：一键设置改不了 npm，不能说设置后就好
            var f = new TerminalProxySnapshot();
            f.Candidates.Add(new ProxyCandidate { Port = 1080, Socks5 = true });
            var npm = new ToolProxySetting { Tool = ProxyTool.Npm, Installed = true };
            npm.Values["proxy"] = "http://127.0.0.1:7890";
            f.Tools.Add(npm);
            f.Reachable["127.0.0.1:7890"] = false;

            TerminalProxyReport report = Analyze(f);

            Assert.Equal(CheckStatus.Fail, report.Overall);
            Assert.NotEqual("命令行还没有走代理", report.Title);
            Assert.Contains("npm", report.Detail);

            // 能改正的工具问题：仍说“还没有走代理”，但颜色是失效，并提到 Git
            TerminalProxySnapshot g = V2rayN();
            g.Tools[0].Values["http.proxy"] = "http://127.0.0.1:7890";
            g.Reachable["127.0.0.1:7890"] = false;
            report = Analyze(g);
            Assert.Equal("命令行还没有走代理", report.Title);
            Assert.Equal(CheckStatus.Fail, report.Overall);
            Assert.Contains("Git", report.Detail);
        }

        [Fact]
        public void Git_EmptyHttpProxyDisablesProxying()
        {
            // git config --global http.proxy ""：Git 不走任何代理，连环境变量也不读
            TerminalProxySnapshot f = V2rayN();
            f.UserEnvironment["HTTP_PROXY"] = f.UserEnvironment["HTTPS_PROXY"] = "http://127.0.0.1:10809";
            f.UserEnvironment["NO_PROXY"] = "localhost";
            f.Tools[0].Values["http.proxy"] = "";

            TerminalProxyReport report = Analyze(f);

            Assert.Equal("已禁用", Row(report, "git").Badge);
            Assert.NotEqual(CheckStatus.Ok, report.Overall);
            ProxyChange change = Assert.Single(report.ApplyChanges);
            Assert.Equal("Git http.proxy = http://127.0.0.1:10809", change.ToString());
            Assert.DoesNotContain(report.ClearChanges, c => c.Target == ProxyChangeTarget.Git);

            // 原来的空值要备份：清除时恢复为空，而不是删掉（删掉后 Git 会改走环境变量里的代理）
            var backup = new TerminalProxyBackup();
            backup.Record(TerminalProxyBackup.KeyOf(change.Target, change.Key), change.OldValue, change.NewValue);
            Assert.Equal("", backup.PreviousOf("Git:http.proxy"));
            TerminalProxyBackup read = IP_UpdateTest.Core.JsonFile.Deserialize<TerminalProxyBackup>(IP_UpdateTest.Core.JsonFile.Serialize(backup));
            f.Tools[0].Values["http.proxy"] = change.NewValue;
            ProxyChange restore = Analyze(f, read).ClearChanges.Single(c => c.Target == ProxyChangeTarget.Git);
            Assert.Equal("", restore.NewValue);
            Assert.Equal("Git http.proxy = \"\"", restore.ToString());

            // 没有代理时 Git 直连是对的
            var g = new TerminalProxySnapshot();
            g.Tools.Add(new ToolProxySetting { Tool = ProxyTool.Git, Installed = true });
            g.Tools[0].Values["http.proxy"] = "";
            Assert.Equal(CheckStatus.Info, Row(Analyze(g), "git").Status);

            // 只对某个网站生效的代理优先于空的 http.proxy：它失效了 Git 访问这个网站会失败，不能说直连
            g.Tools[0].Values["http.https://github.com.proxy"] = "http://127.0.0.1:7890";
            g.Reachable["127.0.0.1:7890"] = false;
            report = Analyze(g);
            Assert.Equal(CheckStatus.Fail, Row(report, "git").Status);
            Assert.Equal(CheckStatus.Fail, report.Overall);

            // 网站的代理能用时：仍提示 http.proxy 为空
            g.Reachable["127.0.0.1:7890"] = true;
            TerminalProxyRow row = Row(Analyze(g), "git");
            Assert.Equal("直连", row.Badge);
            Assert.Contains("单独设置了代理的网站", row.Detail);
        }

        [Fact]
        public void Follower_KeepsFollowingToolsAfterRestoringTheirOriginal()
        {
            // 一键设置把 Git 的公司代理改成了 7890；系统代理关闭时恢复公司代理，再开时要重新改成代理，不能停在公司代理上
            const string key = "Git:http.proxy", corp = "http://proxy.corp:8080";
            var backup = new TerminalProxyBackup();
            backup.Record(key, corp, "http://127.0.0.1:7890");
            var git = new ToolProxySetting { Tool = ProxyTool.Git, Installed = true };
            git.Values["http.proxy"] = "http://127.0.0.1:7890";

            var off = new TerminalProxySnapshot();
            off.Tools.Add(git);
            ProxyChange restore = TerminalProxyFollower.Decide(off, backup).Single(c => c.Target == ProxyChangeTarget.Git);
            Assert.Equal(corp, restore.NewValue);
            // 跟随恢复后仍记作本程序管理的（TerminalProxyConfigurator.Apply 的 following 分支）
            backup.Record(key, restore.OldValue, restore.NewValue);
            git.Values["http.proxy"] = corp;

            var on = new TerminalProxySnapshot { SystemProxyEnabled = true, SystemProxyServer = "127.0.0.1:7897" };
            on.Tools.Add(git);
            Assert.Contains(TerminalProxyFollower.Decide(on, backup), c => c.Target == ProxyChangeTarget.Git && c.NewValue == "http://127.0.0.1:7897");
            Assert.Equal(corp, backup.PreviousOf(key));

            // 只对某个网站生效的项同理：本程序写过的不当作用户的分流
            var site = new ToolProxySetting { Tool = ProxyTool.Git, Installed = true };
            site.Values["http.https://git.corp.example.proxy"] = corp;
            backup.Record("Git:http.https://git.corp.example.proxy", "http://127.0.0.1:7890", corp);
            var on2 = new TerminalProxySnapshot { SystemProxyEnabled = true, SystemProxyServer = "127.0.0.1:7897" };
            on2.Tools.Add(site);
            Assert.Contains(TerminalProxyFollower.Decide(on2, backup), c => c.Key == "http.https://git.corp.example.proxy");
        }

        [Fact]
        public void Git_HttpsSectionIsIgnoredEntirely()
        {
            // Git 没有 https 这一节：https.https://github.com.proxy 失效也不影响 Git
            TerminalProxySnapshot f = V2rayN();
            f.UserEnvironment["HTTP_PROXY"] = f.UserEnvironment["HTTPS_PROXY"] = "http://127.0.0.1:10809";
            f.UserEnvironment["NO_PROXY"] = "localhost";
            f.Tools[0].Values["https.https://github.com.proxy"] = "socks5://127.0.0.1:1080";
            f.Reachable["127.0.0.1:1080"] = false;

            TerminalProxyReport report = Analyze(f);

            Assert.Equal(CheckStatus.Info, Row(report, "git").Status);
            Assert.Equal(CheckStatus.Ok, report.Overall);
            Assert.Empty(report.ApplyChanges);
            Assert.True(ToolProxySetting.IsIgnored(ProxyTool.Git, "https.proxy"));
            Assert.False(ToolProxySetting.IsIgnored(ProxyTool.Git, "http.https://github.com.proxy"));
        }

        [Fact]
        public void Git_SiteRouteToAnotherProxyIsLeftAlone()
        {
            // 公司 Git 服务器走公司代理是有意的分流：不算不一致，一键设置不改
            TerminalProxySnapshot f = V2rayN();
            f.UserEnvironment["HTTP_PROXY"] = f.UserEnvironment["HTTPS_PROXY"] = "http://127.0.0.1:10809";
            f.UserEnvironment["NO_PROXY"] = "localhost";
            f.Tools[0].Values["http.https://git.corp.example.proxy"] = "http://proxy.corp:8080";
            f.Reachable["proxy.corp:8080"] = true;

            TerminalProxyReport report = Analyze(f);

            Assert.Equal(CheckStatus.Ok, Row(report, "git").Status);
            Assert.Equal(CheckStatus.Ok, report.Overall);
            Assert.Empty(report.ApplyChanges);

            // 全局的 http.proxy 指向别的代理仍算不一致
            f.Tools[0].Values["http.proxy"] = "http://proxy.corp:8080";
            Assert.Equal("不一致", Row(Analyze(f), "git").Badge);
        }

        [Fact]
        public void WorkingEnvironmentWithoutKnownProxy_IsNotDirect()
        {
            var f = new TerminalProxySnapshot();
            f.UserEnvironment["HTTP_PROXY"] = f.UserEnvironment["HTTPS_PROXY"] = "http://192.168.1.5:7890";
            f.Reachable["192.168.1.5:7890"] = true;

            TerminalProxyReport report = Analyze(f);

            Assert.Equal("命令行已设置代理", report.Title);
            Assert.DoesNotContain("直连", report.Detail);
        }
    }
}
