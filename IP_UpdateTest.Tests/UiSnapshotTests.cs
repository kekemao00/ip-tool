using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Windows.Forms;
using IP_UpdateTest.Core;
using IP_UpdateTest.Core.Diagnosis;
using IP_UpdateTest.Core.TerminalProxy;
using IP_UpdateTest.Core.Update;
using IP_UpdateTest.Ui;
using Xunit;

namespace IP_UpdateTest.Tests
{
    /// <summary>
    /// 界面冒烟测试：把各窗口和浮层面板真正创建、显示、绘制一遍（动画时间冻结在终点）。
    /// 设置环境变量 UI_SNAPSHOT_DIR 时把截图存成 PNG，供人工检查
    /// </summary>
    [Collection("ProfileStore")]
    public class UiSnapshotTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "IPToolTests", Guid.NewGuid().ToString("N"));
        private readonly string outputDirectory = Environment.GetEnvironmentVariable("UI_SNAPSHOT_DIR");
        private double clock = 1000;

        public UiSnapshotTests()
        {
            Directory.CreateDirectory(directory);
            ProfileManager.UseStorePath(Path.Combine(directory, "profiles.json"));
            if (!string.IsNullOrEmpty(outputDirectory)) Directory.CreateDirectory(outputDirectory);
        }

        public void Dispose()
        {
            Motion.Unfreeze();
            try
            {
                Directory.Delete(directory, true);
            }
            catch (IOException)
            {
            }
        }

        [Fact]
        public void RenderAllWindows()
        {
            RunOnSta(() =>
            {
                // 与 Program.Main 一致；输入框的占位提示需要新版控件库
                Application.EnableVisualStyles();
                Motion.Freeze(clock);
                ProfileManager.Add(new IpProfile { Name = "公司", IsDhcp = false, IpAddress = "10.20.30.40", SubnetMask = "255.255.255.0", Gateway = "10.20.30.1", ManualDns = true, DnsMain = "223.5.5.5", DnsBackup = "119.29.29.29" });
                ProfileManager.Add(new IpProfile { Name = "家里", IsDhcp = true, ManualDns = false });
                ProfileManager.Add(new IpProfile { Name = "实验室", IsDhcp = false, IpAddress = "192.168.8.20", SubnetMask = "255.255.255.0", Gateway = "192.168.8.1", ManualDns = true, DnsMain = "192.168.8.1", AdapterMac = "00-15-5D-01-02-03" });

                List<NetworkAdapter> adapters = SampleAdapters();
                RenderMain(adapters);
                RenderUpdate(adapters);
                RenderProfiles(adapters);
                RenderDnsBenchmark();
                RenderDiagnosis();
                RenderTerminalProxy();
                RenderInfo();
            });
        }

        private void RenderMain(List<NetworkAdapter> adapters)
        {
            using (var form = new FrmMain(null, false, new AppSettings(), false) { SnapshotMode = true })
            {
                Open(form);
                form.BindAdapterList(adapters);
                Save(form, "main");

                form.Notify(NoticeKind.Success, "已应用到“以太网”");
                Save(form, "main-notice", 0.6);

                form.OpenCommandPalette();
                Save(form, "main-palette");
                CloseSheets(form);

                var confirm = form.ConfirmAsync(null, "删除配置方案", "确定删除“公司”？删除后不能恢复。", "删除", true);
                Save(form, "main-confirm");
                CloseSheets(form);
                Assert.True(confirm.IsCompleted);

                // IPv6 页：第一个网卡手动设置了 IPv6
                form.ShowIpv6Page();
                Save(form, "main-ipv6");

                // 自动获取：有公网、临时、本地链接地址（完整的 8 段地址要能完整显示）
                form.BindAdapterList(new List<NetworkAdapter> { Ipv6Adapter(true) });
                Save(form, "main-ipv6-auto");

                // 网络没有 IPv6（如虚拟机的 NAT 网络）：只有本地链接地址，给出说明而不是一片空白
                form.BindAdapterList(new List<NetworkAdapter> { Ipv6Adapter(false) });
                Save(form, "main-ipv6-none");
                // 左侧卡片：放得下的地址不省略
                TextBox ipv6List = Descendants(form).OfType<TextBox>().Single(t => t.Multiline && t.ReadOnly && t.ScrollBars == ScrollBars.None);
                Assert.Contains("fe80::a32c:7c34:a627:f521", ipv6List.Text);
            }

            // 管理员权限下（README 用）：没有提权提示条，标题栏显示“管理员”
            using (var form = new FrmMain(null, false, new AppSettings(), true) { SnapshotMode = true })
            {
                Open(form);
                form.BindAdapterList(adapters);
                Save(form, "main-admin");

                form.OpenCommandPalette();
                Save(form, "main-admin-palette");
                CloseSheets(form);

                form.ShowIpv6Page();
                form.BindAdapterList(new List<NetworkAdapter> { Ipv6Adapter(true) });
                Save(form, "main-admin-ipv6");
            }
        }

        /// <summary>
        /// 应用内更新：标题栏胶囊和更新面板的各个状态
        /// </summary>
        private void RenderUpdate(List<NetworkAdapter> adapters)
        {
            var release = new ReleaseInfo
            {
                Version = new Version(9, 1, 0),
                Tag = "v9.1.0",
                PublishedAt = new DateTime(2026, 10, 20, 16, 0, 0),
                PackageName = "IPTool-v9.1.0.zip",
                PackageSize = 452608,
                PageUrl = "https://github.com/kekemao00/ip-tool/releases/tag/v9.1.0",
                Notes = "### 新功能\n\n- 应用内更新：自动检查 GitHub 上的新版本，在程序里下载、校验并替换，重启即用\n- 标题栏出现更新提示，不弹窗打扰；下载可以在后台继续\n\n### 改进\n\n- 安装包经过 SHA-256 校验，替换失败时自动恢复原来的程序\n- 程序在受保护的文件夹中时，只在替换文件时请求一次管理员权限\n- 配置方案和设置都保留在 %APPDATA%\\IPTool\n\n## What's Changed\n* 应用内更新 by @someone"
            };
            using (var form = new FrmMain(null, false, new AppSettings(), true) { SnapshotMode = true })
            {
                Open(form);
                form.BindAdapterList(adapters);
                form.Updates.Simulate(UpdateState.Available, release);
                Save(form, "main-update");

                form.OpenUpdateSheet();
                Save(form, "update-available");

                form.Updates.Simulate(UpdateState.Downloading, release, new DownloadProgress(190464, 452608, 1310720));
                Save(form, "update-downloading");

                form.Updates.Simulate(UpdateState.Ready, release);
                Save(form, "update-ready");

                form.Updates.Simulate(UpdateState.Failed, release, default(DownloadProgress), "下载超时，请检查网络或代理后重试");
                Save(form, "update-failed");
                CloseSheets(form);

                form.Updates.Simulate(UpdateState.Downloading, release, new DownloadProgress(301000, 452608, 1310720));
                Save(form, "main-update-downloading");
            }
        }

        private void RenderProfiles(List<NetworkAdapter> adapters)
        {
            using (var form = new FrmProfiles(adapters))
            {
                Open(form);
                Save(form, "profiles");

                var task = SheetOverlay.Show(form, new ProfileEditSheet(form, ProfileManager.Profiles[0], adapters), null);
                Save(form, "profiles-edit");
                CloseSheets(form);
                Assert.True(task.IsCompleted);
            }
        }

        private void RenderDnsBenchmark()
        {
            var candidates = ProfileManager.PresetDns.Select(p => new DnsCandidate(p.Key, p.Value)).ToList();
            using (var form = new FrmDnsBenchmark(candidates, false))
            {
                Open(form);
                var results = new Dictionary<TableRow, long?>();
                long ms = 9;
                foreach (TableRow row in form.Rows)
                {
                    long? value = row == form.Rows.Last() ? (long?)null : ms;
                    ms += 7;
                    row.Cells[3] = value.HasValue ? value + " ms / " + (value + 4) + " ms" : "超时";
                    results[row] = value;
                }
                form.ShowResults(results);
                Save(form, "dns-benchmark", 1.2);
            }
        }

        private void RenderDiagnosis()
        {
            var report = new DiagnosisReport { GeneratedAt = new DateTime(2026, 10, 6, 9, 30, 0), AdapterName = "WLAN" };
            report.Checks.Add(new DiagnosisCheck(DiagnosisLayer.Adapter, "连接状态", CheckStatus.Ok, "WLAN 已连接，866 Mbps"));
            report.Checks.Add(new DiagnosisCheck(DiagnosisLayer.Gateway, "连通性", CheckStatus.Ok, "192.168.1.1 平均 2 ms"));
            report.Checks.Add(new DiagnosisCheck(DiagnosisLayer.Internet, "外网", CheckStatus.Ok, "223.5.5.5 平均 12 ms"));
            report.Checks.Add(new DiagnosisCheck(DiagnosisLayer.Dns, "DNS 192.168.1.1", CheckStatus.Fail, "无响应", "重启路由器", "把 DNS 改成 223.5.5.5"));
            report.Checks.Add(new DiagnosisCheck(DiagnosisLayer.ProxyVpn, "系统代理", CheckStatus.Warning, "已开启代理 127.0.0.1:7890", "确认代理软件在运行"));
            report.Checks.Add(new DiagnosisCheck(DiagnosisLayer.Udp, "UDP", CheckStatus.Skipped, "DNS 不通，未检测"));
            using (var form = new FrmDiagnosis(null, report))
            {
                Open(form);
                Save(form, "diagnosis", 1.5);
            }
        }

        private void RenderTerminalProxy()
        {
            // v2rayN 只开了系统代理，Git 还留着以前 Clash 的端口
            var f = new TerminalProxySnapshot { GeneratedAt = new DateTime(2026, 10, 16, 9, 30, 0), SystemProxyEnabled = true, SystemProxyServer = "127.0.0.1:10809" };
            f.Clients.Add("v2rayN");
            f.Candidates.Add(new ProxyCandidate { Port = 10809, Http = true, Owner = "v2rayN", IsSystemProxy = true });
            f.Candidates.Add(new ProxyCandidate { Port = 10808, Socks5 = true, Owner = "v2rayN" });
            f.Reachable["127.0.0.1:10809"] = true;
            f.Reachable["127.0.0.1:7890"] = false;
            f.Tunnels["127.0.0.1:10809"] = 186;
            var git = new ToolProxySetting { Tool = ProxyTool.Git, Installed = true };
            git.Values["http.proxy"] = "http://127.0.0.1:7890";
            f.Tools.Add(git);
            f.Tools.Add(new ToolProxySetting { Tool = ProxyTool.Npm, Installed = true });
            f.Tools.Add(new ToolProxySetting { Tool = ProxyTool.Pip, Installed = true });
            using (var form = new FrmTerminalProxy(new AppSettings(), null, f))
            {
                Open(form);
                Save(form, "terminal-proxy", 1.5);
            }

            // 已全部设置好
            f.UserEnvironment["HTTP_PROXY"] = f.UserEnvironment["HTTPS_PROXY"] = "http://127.0.0.1:10809";
            f.UserEnvironment["NO_PROXY"] = TerminalProxyAnalyzer.DefaultNoProxy;
            git.Values["http.proxy"] = "http://127.0.0.1:10809";
            using (var form = new FrmTerminalProxy(new AppSettings { FollowSystemProxy = true }, null, f))
            {
                Open(form);
                Save(form, "terminal-proxy-ok", 1.5);
            }

            // 手动添加的 SSH 隧道（ssh -D 1081），没有代理软件
            var ssh = new TerminalProxySnapshot { GeneratedAt = new DateTime(2026, 11, 25, 9, 30, 0) };
            var manual = new ProxyCandidate { Port = 1081, Socks5 = true, IsManual = true };
            ssh.Candidates.Add(manual);
            ssh.Reachable["127.0.0.1:1081"] = true;
            ssh.Tools.Add(new ToolProxySetting { Tool = ProxyTool.Git, Installed = true });
            using (var form = new FrmTerminalProxy(new AppSettings { ManualProxy = manual.ManualUrl }, null, ssh))
            {
                Open(form);
                Save(form, "terminal-proxy-manual", 1.5);

                var task = SheetOverlay.Show(form, new ManualProxySheet(manual), null);
                Save(form, "terminal-proxy-manual-sheet");
                CloseSheets(form);
                Assert.True(task.IsCompleted);
            }
        }

        private void RenderInfo()
        {
            using (var form = new FrmInfo())
            {
                Open(form);
                // 网卡信息在后台线程读取，等它读完
                Pump(3000, () => form.Controls.OfType<Card>().SelectMany(c => c.Controls.OfType<TextBox>()).All(t => !t.Text.StartsWith("正在读取", StringComparison.Ordinal)));
                Save(form, "info");
            }
        }

        private static List<NetworkAdapter> SampleAdapters()
        {
            return new List<NetworkAdapter>
            {
                new NetworkAdapter
                {
                    NetworkInterfaceID = "{A}",
                    Name = "以太网",
                    InterfaceDescription = "Realtek PCIe 2.5GbE Family Controller",
                    Status = AdapterStatus.Disconnected,
                    IsPhysical = true,
                    IsDhcpEnabled = false,
                    ConfigBackend = BackendKind.Wmi,
                    ConfiguredIpAddress = "192.168.1.20",
                    ConfiguredSubnetMask = "255.255.255.0",
                    ConfiguredGateway = "192.168.1.1",
                    ConfiguredDns = new[] { "223.5.5.5", "119.29.29.29" },
                    MacAddress = PhysicalAddress.Parse("00-15-5D-01-02-03"),
                    InterfaceIndex = 12,
                    Ipv6Enabled = true,
                    Ipv6ConfigKnown = true,
                    Ipv6ManualAddresses = new List<Ipv6ManualAddress> { new Ipv6ManualAddress("2001:db8:1::20", 64) },
                    Ipv6ManualGateways = new List<string> { "fe80::1" },
                    ConfiguredIpv6Dns = new[] { "2400:3200::1", "2400:3200:baba::1" }
                },
                new NetworkAdapter
                {
                    NetworkInterfaceID = "{B}",
                    Name = "WLAN",
                    InterfaceDescription = "Intel(R) Wi-Fi 6 AX201 160MHz",
                    Status = AdapterStatus.Disconnected,
                    IsPhysical = true,
                    IsDhcpEnabled = true,
                    ConfigBackend = BackendKind.Wmi,
                    MacAddress = PhysicalAddress.Parse("A4-B1-C1-11-22-33")
                }
            };
        }

        /// <summary>
        /// 已连接、IPv6 自动获取的网卡；routable 为 false 时只有本地链接地址
        /// </summary>
        private static NetworkAdapter Ipv6Adapter(bool routable)
        {
            var adapter = new NetworkAdapter
            {
                NetworkInterfaceID = routable ? "{C}" : "{D}",
                Name = "以太网",
                InterfaceDescription = routable ? "Intel(R) Ethernet Connection I219-V" : "vmxnet3 以太网适配器",
                Status = AdapterStatus.Connected,
                LinkSpeedBps = 1000000000,
                IsPhysical = true,
                IsDhcpEnabled = true,
                ConfigBackend = BackendKind.Wmi,
                InterfaceIndex = 7,
                MacAddress = PhysicalAddress.Parse("00-0C-29-E8-CD-C0"),
                Ipv6Enabled = true,
                Ipv6ConfigKnown = true
            };
            if (routable)
            {
                adapter.Ipv6LiveAddresses.Add(new Ipv6AddressInfo("2408:8207:24a1:5c00:a32c:7c34:a627:f521", 64, Ipv6AddressKind.Global));
                adapter.Ipv6LiveAddresses.Add(new Ipv6AddressInfo("2408:8207:24a1:5c00:9f3c:11d2:6b0e:4a7d", 64, Ipv6AddressKind.Temporary));
                adapter.Ipv6LiveAddresses.Add(new Ipv6AddressInfo("2408:8207:24a1:5c00:1d4e:88a0:3c71:e2b9", 64, Ipv6AddressKind.Temporary));
                adapter.Ipv6LiveGateways.Add("fe80::1");
                adapter.Ipv6LiveDns.AddRange(new[] { "2408:8000:1010:1::8", "2408:8000:1010:2::8" });
            }
            adapter.Ipv6LiveAddresses.Add(new Ipv6AddressInfo("fe80::a32c:7c34:a627:f521", 64, Ipv6AddressKind.LinkLocal));
            return adapter;
        }

        private void Open(Form form)
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(40, 40);
            form.ShowInTaskbar = false;
            form.Show();
            Pump(200, null);
        }

        /// <summary>
        /// 把冻结的时钟往后拨，让动画走到终点，再截图
        /// </summary>
        private void Save(Form form, string name, double settle = 1.0)
        {
            clock += settle;
            Motion.Freeze(clock);
            form.PerformLayout();
            form.Invalidate(true);
            form.Update();
            Pump(150, null);
            AssertTextFits(form, name);
            using (Bitmap bitmap = WindowCapture.Client(form))
            {
                Assert.True(bitmap.Width > 100 && bitmap.Height > 100);
                // 不是一片空白：至少要有墨色的像素（文字、图标）
                Assert.True(HasInk(bitmap), name + " 没有画出内容");
                if (!string.IsNullOrEmpty(outputDirectory)) bitmap.Save(Path.Combine(outputDirectory, name + ".png"), ImageFormat.Png);
            }
        }

        /// <summary>
        /// 原生输入框里的文字放不下时直接截断、没有省略号：输入框的占位提示、不能滚动的只读信息（如 IPv6 地址）
        /// 都要完整显示（含未显示的那一页和打开着的面板）
        /// </summary>
        private static void AssertTextFits(Control root, string name)
        {
            foreach (Control c in root.Controls)
            {
                var field = c as FieldBox;
                if (field != null && field.Placeholder.Length > 0)
                    AssertFits(field.Box, field.Placeholder, name, "占位提示");
                var box = c as TextBox;
                if (box != null && box.ReadOnly && box.ScrollBars == ScrollBars.None)
                {
                    foreach (string line in box.Lines) AssertFits(box, line, name, "文字");
                }
                AssertTextFits(c, name);
            }
        }

        private static IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control c in root.Controls)
            {
                yield return c;
                foreach (Control d in Descendants(c)) yield return d;
            }
        }

        private static void AssertFits(TextBox box, string text, string name, string what)
        {
            // 原生输入框左右各有几像素的内边距
            int available = box.ClientSize.Width - 6;
            int needed = TextRenderer.MeasureText(text, box.Font, Size.Empty, TextFormatFlags.NoPadding).Width;
            Assert.True(needed <= available, $"{name}：{what}“{text}”放不下（需要 {needed}px，只有 {available}px）");
        }

        private void CloseSheets(ThemedForm form)
        {
            foreach (SheetOverlay overlay in form.Controls.OfType<SheetOverlay>().ToList()) overlay.Close(null);
            clock += 1;
            Motion.Freeze(clock);
            // 时钟冻结时帧时钟不推进，这里直接把收起的浮层移除
            foreach (SheetOverlay overlay in form.Controls.OfType<SheetOverlay>().ToList()) overlay.Finish();
            Pump(50, null);
        }

        private static bool HasInk(Bitmap bitmap)
        {
            for (int y = 0; y < bitmap.Height; y += 3)
            {
                for (int x = 0; x < bitmap.Width; x += 3)
                {
                    Color c = bitmap.GetPixel(x, y);
                    if (c.R < 80 && c.G < 80 && c.B < 80) return true;
                }
            }
            return false;
        }

        private static void Pump(int milliseconds, Func<bool> until)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < milliseconds)
            {
                Application.DoEvents();
                if (until != null && until()) break;
                Thread.Sleep(10);
            }
        }

        private static void RunOnSta(Action action)
        {
            Exception error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error != null) throw new Exception("界面渲染失败：" + error.Message, error);
        }
    }
}
