using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using IP_UpdateTest.Core;
using IP_UpdateTest.Core.Update;
using IP_UpdateTest.Ui;

namespace IP_UpdateTest
{
    /// <summary>
    /// 应用内更新：启动后和之后每隔约一天在后台检查 GitHub Releases；有新版本时标题栏出现更新胶囊（不弹窗打扰），
    /// 点开是更新面板：看更新内容 → 下载（可在后台继续）→ SHA-256 校验 → 重启并替换。
    /// 入口还有“更多”菜单、托盘菜单和命令面板
    /// </summary>
    public partial class FrmMain
    {
        /// <summary>
        /// 两次自动检查的最短间隔
        /// </summary>
        private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(20);

        /// <summary>
        /// 启动后等这么久再检查，不和读取网卡抢时间
        /// </summary>
        private const int FirstUpdateCheckDelay = 4000;

        private readonly System.Windows.Forms.Timer updateTimer = new System.Windows.Forms.Timer();
        private UpdateManager updates;
        private UpdateState lastUpdateState;

        /// <summary>
        /// 自动检查到的是用户跳过的版本：不显示更新胶囊
        /// </summary>
        private bool updateBadgeSuppressed;

        /// <summary>
        /// 已经用通知提示过的版本（每个版本只提示一次）
        /// </summary>
        private string notifiedUpdateTag;

        private bool startupUpdateCheckDone;

        /// <summary>
        /// 从旧版本更新后首次启动时，为更新前的版本号（--updated-from）
        /// </summary>
        internal string UpdatedFrom { get; set; }

        internal UpdateManager Updates
        {
            get { return updates; }
        }

        private void InitUpdates()
        {
            updates = new UpdateManager(Versions.Running, new UpdateClient());
            updates.Changed += OnUpdateChanged;
            TitleBar.UpdateClicked += (s, e) => OpenUpdateSheet();
            updateTimer.Tick += UpdateTimer_Tick;
            Load += (s, e) =>
            {
                if (!SnapshotMode) StartUpdateChecks();
            };
            Shown += (s, e) =>
            {
                if (!SnapshotMode) AnnounceCompletedUpdate();
            };
            FormClosed += (s, e) =>
            {
                updateTimer.Dispose();
                if (updates.State == UpdateState.Downloading || updates.State == UpdateState.Verifying) updates.CancelDownload();
            };
        }

        private void StartUpdateChecks()
        {
            // 上次更新留下的 IPTool.exe.old 等
            UpdateInstaller.CleanupBackups(Path.GetDirectoryName(Application.ExecutablePath));
            updateTimer.Interval = FirstUpdateCheckDelay;
            updateTimer.Start();
        }

        /// <summary>
        /// 更新后首次启动：提示已更新，清理下载的文件
        /// </summary>
        private void AnnounceCompletedUpdate()
        {
            if (UpdatedFrom == null) return;
            UpdateClient.CleanupStaging();
            Version from = Versions.Parse(UpdatedFrom);
            if (from == null || !Versions.IsNewer(updates.CurrentVersion, from)) return;
            settings.SkippedVersion = null;
            SaveSettings();
            // 等入场动画走完再弹通知
            var timer = new System.Windows.Forms.Timer { Interval = 450 };
            timer.Tick += (s, e) =>
            {
                timer.Dispose();
                if (!IsDisposed) Notify(NoticeKind.Success, "已更新到 " + Versions.Display(updates.CurrentVersion));
            };
            timer.Start();
        }

        private async void UpdateTimer_Tick(object sender, EventArgs e)
        {
            // 之后每小时看一次是否到了检查时间
            updateTimer.Interval = (int)TimeSpan.FromHours(1).TotalMilliseconds;
            await CheckForUpdatesAsync(false);
        }

        /// <summary>
        /// 检查更新。手动检查时显示结果，有新版本直接打开更新面板；自动检查只在发现新版本时轻轻提示
        /// </summary>
        private async Task CheckForUpdatesAsync(bool manual)
        {
            if (manual)
            {
                if (updates.HasUpdate)
                {
                    updateBadgeSuppressed = false;
                    OnUpdateChanged();
                    OpenUpdateSheet();
                    return;
                }
                if (updates.State == UpdateState.Checking) return;
                Notify(NoticeKind.Info, "正在检查更新…");
            }
            else
            {
                if (settings.UpdateCheckDisabled || updates.IsBusy || updates.HasUpdate) return;
                bool due = !startupUpdateCheckDone || !settings.LastUpdateCheck.HasValue
                    || DateTime.Now - settings.LastUpdateCheck.Value >= UpdateCheckInterval;
                if (!due) return;
                startupUpdateCheckDone = true;
            }

            ReleaseInfo release;
            try
            {
                release = await updates.CheckAsync(CancellationToken.None);
            }
            catch (UpdateException ex)
            {
                if (manual && !IsDisposed) Notify(NoticeKind.Error, "检查更新失败：" + ex.Message);
                return;
            }
            if (IsDisposed) return;
            settings.LastUpdateCheck = DateTime.Now;
            SaveSettings();

            if (release == null)
            {
                if (manual) Notify(NoticeKind.Success, "已是最新版本 " + Versions.Display(updates.CurrentVersion));
                return;
            }
            if (manual)
            {
                updateBadgeSuppressed = false;
                OnUpdateChanged();
                OpenUpdateSheet();
                return;
            }
            updateBadgeSuppressed = string.Equals(release.Tag, settings.SkippedVersion, StringComparison.OrdinalIgnoreCase);
            OnUpdateChanged();
            if (!updateBadgeSuppressed && notifiedUpdateTag != release.Tag && Visible && !HasSheet)
            {
                notifiedUpdateTag = release.Tag;
                Notify(NoticeKind.Info, "发现新版本 " + release.DisplayVersion + "，点右上角查看");
            }
        }

        /// <summary>
        /// 状态变化：刷新标题栏胶囊、托盘提示；后台下载完成或失败时提示一次
        /// </summary>
        private void OnUpdateChanged()
        {
            if (IsDisposed) return;
            UpdateState state = updates.State;
            ReleaseInfo release = updates.Release;
            string version = release == null ? "" : release.DisplayVersion;

            if (!updates.HasUpdate || (updateBadgeSuppressed && state == UpdateState.Available))
            {
                TitleBar.SetUpdateBadge(UpdateBadgeKind.None, null, 0, null);
            }
            else
            {
                switch (state)
                {
                    case UpdateState.Downloading:
                        int percent = (int)Math.Floor(updates.Progress.Fraction * 100);
                        TitleBar.SetUpdateBadge(UpdateBadgeKind.Progress, "正在下载 " + percent + "%", updates.Progress.Fraction, "点击查看下载进度");
                        break;
                    case UpdateState.Verifying:
                        TitleBar.SetUpdateBadge(UpdateBadgeKind.Progress, "正在校验", 1, "正在校验安装包");
                        break;
                    case UpdateState.Ready:
                        TitleBar.SetUpdateBadge(UpdateBadgeKind.Ready, "重启以更新", 1, version + " 已下载，点击重启并更新");
                        break;
                    case UpdateState.Installing:
                        TitleBar.SetUpdateBadge(UpdateBadgeKind.Progress, "正在更新", 1, "正在替换程序文件");
                        break;
                    case UpdateState.Failed:
                        TitleBar.SetUpdateBadge(UpdateBadgeKind.Failed, "更新失败", 0, updates.Error);
                        break;
                    default:
                        TitleBar.SetUpdateBadge(UpdateBadgeKind.Available, "新版本 " + version, 0, version + " 已发布，点击查看更新内容");
                        break;
                }
            }

            if (trayIcon != null)
                trayIcon.Text = updates.HasUpdate && !updateBadgeSuppressed ? AppTitle + "（" + version + " 可更新）" : AppTitle;

            // 面板关着时，后台下载的结果用通知告诉用户
            if (state != lastUpdateState && Visible && !HasSheet)
            {
                if (state == UpdateState.Ready && (lastUpdateState == UpdateState.Verifying || lastUpdateState == UpdateState.Downloading))
                    Notify(NoticeKind.Success, version + " 已下载好，点右上角重启更新");
                else if (state == UpdateState.Failed && lastUpdateState != UpdateState.Installing)
                    Notify(NoticeKind.Error, updates.Error);
            }
            lastUpdateState = state;
        }

        /// <summary>
        /// 打开更新面板（从标题栏的更新胶囊展开）
        /// </summary>
        internal async void OpenUpdateSheet()
        {
            if (HasSheet || !updates.HasUpdate) return;
            if (!Visible || WindowState == FormWindowState.Minimized) ShowFromTray();
            var sheet = new UpdateSheet(updates, SkipUpdate, InstallUpdateAsync, OpenReleasePage);
            System.Drawing.Rectangle origin = TitleBar.UpdateBadgeBounds;
            if (origin.IsEmpty) await SheetOverlay.Show(this, sheet, null);
            else await SheetOverlay.Show(this, sheet, origin, Palette.AccentSoft, origin.Height / 2f);
        }

        private void SkipUpdate()
        {
            ReleaseInfo release = updates.Release;
            if (release == null) return;
            settings.SkippedVersion = release.Tag;
            SaveSettings();
            updateBadgeSuppressed = true;
            OnUpdateChanged();
            Notify(NoticeKind.Info, "已跳过 " + release.DisplayVersion + "，有更新的版本时再提醒你");
        }

        private void OpenReleasePage()
        {
            ReleaseInfo release = updates.Release;
            try
            {
                Process.Start(new ProcessStartInfo { FileName = release != null && release.PageUrl != null ? release.PageUrl : UpdateClient.ReleasesPage, UseShellExecute = true });
            }
            catch (Win32Exception)
            {
                Notify(NoticeKind.Warning, "无法打开浏览器");
            }
        }

        /// <summary>
        /// 替换程序文件并重启到新版本；未应用的修改会暂存，新版本启动后回填
        /// </summary>
        private async Task InstallUpdateAsync()
        {
            if (updates.State != UpdateState.Ready) return;
            if (isBusy)
            {
                Notify(NoticeKind.Warning, "正在修改网络配置，完成后再更新");
                return;
            }

            string executable = Application.ExecutablePath;
            StashPendingEdit();
            bool installed;
            try
            {
                installed = await updates.InstallAsync(Path.GetDirectoryName(executable), executable);
            }
            catch (UpdateException ex)
            {
                PendingEdit.Clear();
                if (!HasSheet) Notify(NoticeKind.Error, ex.Message);
                return;
            }
            if (!installed)
            {
                PendingEdit.Clear();
                Notify(NoticeKind.Info, "程序在受保护的文件夹中，需要管理员权限才能更新");
                return;
            }

            NetworkAdapter adapter = SelectedAdapter;
            try
            {
                UpdateInstaller.Relaunch(executable, updates.CurrentVersion, adapter == null ? null : "--adapter " + adapter.NetworkInterfaceID);
            }
            catch (Win32Exception ex)
            {
                await AlertAsync(null, "新版本已安装", "没能自动打开新版本（" + ex.Message + "），请手动重新打开 IP Tool。");
            }
            ExitApplication();
        }

        private ToolStripMenuItem UpdateMenuItem()
        {
            ReleaseInfo release = updates.Release;
            if (updates.State == UpdateState.Ready)
                return Menus.Item("重启并更新到 " + release.DisplayVersion, Glyph.ArrowUpCircle, async (s, e) => await InstallUpdateAsync());
            if (updates.HasUpdate)
                return Menus.Item("更新到 " + release.DisplayVersion + "…", Glyph.ArrowUpCircle, async (s, e) => await CheckForUpdatesAsync(true));
            ToolStripMenuItem item = Menus.Item("检查更新", Versions.Display(updates.CurrentVersion), Glyph.ArrowUpCircle, async (s, e) => await CheckForUpdatesAsync(true));
            item.Enabled = updates.State != UpdateState.Checking;
            return item;
        }

        private ToolStripMenuItem AutoUpdateMenuItem()
        {
            ToolStripMenuItem item = Menus.Item("自动检查更新", Glyph.Refresh, (s, e) => ToggleAutoUpdate());
            item.Checked = !settings.UpdateCheckDisabled;
            return item;
        }

        private void ToggleAutoUpdate()
        {
            settings.UpdateCheckDisabled = !settings.UpdateCheckDisabled;
            SaveSettings();
            SetStatus(settings.UpdateCheckDisabled ? "已关闭自动检查更新，可在“更多”菜单中手动检查" : "已开启自动检查更新", StatusKind.Success);
        }

        private void AddUpdateCommands(List<PaletteCommand> list, string group)
        {
            ReleaseInfo release = updates.Release;
            const string keywords = "update 升级 新版本 版本 更新";
            if (updates.State == UpdateState.Ready)
                list.Add(new PaletteCommand(group, "重启并更新到 " + release.DisplayVersion, Glyph.ArrowUpCircle, async () => await InstallUpdateAsync()) { Keywords = keywords });
            else if (updates.HasUpdate)
                list.Add(new PaletteCommand(group, "更新到 " + release.DisplayVersion, Glyph.ArrowUpCircle, async () => await CheckForUpdatesAsync(true)) { Keywords = keywords });
            else
                list.Add(new PaletteCommand(group, "检查更新", Glyph.ArrowUpCircle, async () => await CheckForUpdatesAsync(true)) { Hint = Versions.Display(updates.CurrentVersion), Keywords = keywords });
            list.Add(new PaletteCommand(group, (settings.UpdateCheckDisabled ? "开启" : "关闭") + "自动检查更新", Glyph.Refresh, ToggleAutoUpdate) { Keywords = keywords });
        }
    }
}
