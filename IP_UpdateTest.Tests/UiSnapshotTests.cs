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
                RenderProfiles(adapters);
                RenderDnsBenchmark();
                RenderDiagnosis();
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
                    MacAddress = PhysicalAddress.Parse("00-15-5D-01-02-03")
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
            using (Bitmap bitmap = WindowCapture.Client(form))
            {
                Assert.True(bitmap.Width > 100 && bitmap.Height > 100);
                // 不是一片空白：至少要有墨色的像素（文字、图标）
                Assert.True(HasInk(bitmap), name + " 没有画出内容");
                if (!string.IsNullOrEmpty(outputDirectory)) bitmap.Save(Path.Combine(outputDirectory, name + ".png"), ImageFormat.Png);
            }
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
