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
            var backup = new TerminalProxyBackup { Written = new List<string> { "HTTPS_PROXY", "NO_PROXY" } };
            backup.Previous["HTTPS_PROXY"] = "http://proxy.corp:8080";

            TerminalProxyReport report = Analyze(f, backup);

            Assert.Contains(report.ClearChanges, c => c.Key == "HTTPS_PROXY" && c.NewValue == "http://proxy.corp:8080");
            Assert.Contains(report.ClearChanges, c => c.Key == "NO_PROXY" && c.NewValue == null);
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
            Assert.Contains("$env:HTTPS_PROXY = \"http://127.0.0.1:10809\"", ShellSnippets.Build(ShellKind.PowerShell, url));
            Assert.Contains("set HTTPS_PROXY=http://127.0.0.1:10809", ShellSnippets.Build(ShellKind.Cmd, url));
            Assert.Contains("export http_proxy=http://127.0.0.1:10809", ShellSnippets.Build(ShellKind.Bash, url));
            Assert.Contains("Remove-Item Env:HTTP_PROXY", ShellSnippets.Build(ShellKind.PowerShell, null));
            Assert.Contains("unset http_proxy", ShellSnippets.Build(ShellKind.Bash, null));
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
        }

        [Fact]
        public void PortProbe_RecognizesProtocols()
        {
            Assert.True(ProxyPortProbe.IsHttpResponse(Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection established")));
            Assert.False(ProxyPortProbe.IsHttpResponse(new byte[] { 5, 0 }));
            Assert.True(ProxyPortProbe.IsSocks5Greeting(new byte[] { 5, 0 }));
            Assert.False(ProxyPortProbe.IsSocks5Greeting(new byte[] { 5, 0xFF }));
            // 端口在表中是网络字节序：10809 = 0x2A39
            Assert.Equal(10809, ProxyPortProbe.NetworkPort(0x392A));
        }
    }
}
