using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows.Forms;
using IP_UpdateTest.Core;

namespace IP_UpdateTest
{
    public partial class FrmMain : ThemedForm
    {
        private const string AppTitle = "网络配置工具";

        /// <summary>
        /// 系统网络变化通常成批出现，等这么久没有新变化再刷新
        /// </summary>
        private const int RefreshDebounceMilliseconds = 500;

        /// <summary>
        /// 应用配置后等系统更新地址信息再刷新
        /// </summary>
        private const int RefreshAfterApplyMilliseconds = 1500;

        private enum StatusKind
        {
            Info,
            Success,
            Error
        }

        private readonly Timer refreshTimer = new Timer();
        private readonly ContextMenuStrip dnsPresetMenu = new ContextMenuStrip();
        private readonly ContextMenuStrip moreMenu = new ContextMenuStrip();
        private readonly ContextMenuStrip profileMenu = new ContextMenuStrip();
        private readonly ContextMenuStrip trayMenu = new ContextMenuStrip();
        private readonly AppSettings settings = AppSettings.Load();
        private NotifyIcon trayIcon;

        /// <summary>
        /// 开机自启时直接缩到托盘
        /// </summary>
        private readonly bool startInTray;

        /// <summary>
        /// 真正退出（而不是隐藏到托盘）
        /// </summary>
        private bool exitRequested;

        /// <summary>
        /// 窗体初始化中，忽略控件的变更事件
        /// </summary>
        private bool isInitializing = true;

        /// <summary>
        /// 当前选中网卡的ID，用于重新加载列表后再次选中（列表顺序可能变化，不能按下标）
        /// </summary>
        private string selectedAdapterId;

        /// <summary>
        /// 表单被用户修改过、尚未应用
        /// </summary>
        private bool isDirty;

        /// <summary>
        /// 正在用代码填充表单，忽略期间的修改事件
        /// </summary>
        private bool isLoadingFields;

        /// <summary>
        /// 正在重新绑定网卡列表，选中事件统一在绑定结束后处理
        /// </summary>
        private bool isBinding;

        /// <summary>
        /// 正在应用配置、启停网卡等
        /// </summary>
        private bool isBusy;

        /// <summary>
        /// 正在读取网卡列表
        /// </summary>
        private bool isRefreshing;

        /// <summary>
        /// 配置方案文件的读取问题只提示一次
        /// </summary>
        private bool profileWarningShown;

        /// <param name="initialAdapterId">启动后默认选中的网卡ID（以管理员身份重启时传入）</param>
        /// <param name="startInTray">启动后只显示托盘图标（开机自启时）</param>
        public FrmMain(string initialAdapterId = null, bool startInTray = false)
        {
            InitializeComponent();
            selectedAdapterId = initialAdapterId;
            this.startInTray = startInTray;
            refreshTimer.Tick += RefreshTimer_Tick;
            if (startInTray)
            {
                // 先以最小化、不占任务栏的方式创建，显示后立即隐藏，避免闪一下
                WindowState = FormWindowState.Minimized;
                ShowInTaskbar = false;
            }
        }

        private NetworkAdapter SelectedAdapter
        {
            get { return cbxNetworkAdapter.SelectedItem as NetworkAdapter; }
        }

        private TextBox[] EditableFields
        {
            get { return new[] { txtIpAddress, txtMask, txtGateway, txtDnsMain, txtDnsBackup }; }
        }

        #region 加载与外观

        private async void FrmMain_Load(object sender, EventArgs e)
        {
            ApplyModernTheme();
            CreateTrayIcon();
            chkShowAll.Checked = settings.ShowAllAdapters;
            isInitializing = false;

            NetworkChange.NetworkAddressChanged += NetworkChange_NetworkAddressChanged;
            NetworkChange.NetworkAvailabilityChanged += NetworkChange_NetworkAvailabilityChanged;

            SetStatus("正在读取网卡信息…");
            await RefreshAdaptersAsync();
            if (IsDisposed) return;
            RestorePendingEdit();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (startInTray)
            {
                Hide();
                WindowState = FormWindowState.Normal;
                ShowInTaskbar = true;
            }
        }

        private void FrmMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            // 开启“关闭时最小化到托盘”时，点关闭只隐藏窗口
            if (!exitRequested && e.CloseReason == CloseReason.UserClosing && settings.MinimizeToTray)
            {
                e.Cancel = true;
                HideToTray();
            }
        }

        private void FrmMain_FormClosed(object sender, FormClosedEventArgs e)
        {
            // 系统网络事件是静态事件，不取消订阅会让窗体无法释放
            NetworkChange.NetworkAddressChanged -= NetworkChange_NetworkAddressChanged;
            NetworkChange.NetworkAvailabilityChanged -= NetworkChange_NetworkAvailabilityChanged;
            refreshTimer.Dispose();
            if (trayIcon != null)
            {
                // 不先隐藏的话，退出后托盘里会残留图标直到鼠标划过
                trayIcon.Visible = false;
                trayIcon.Dispose();
            }
        }

        protected override void WndProc(ref Message m)
        {
            // 用户再次启动程序时，已运行的实例收到广播消息后显示主窗口
            if (m.Msg == SingleInstance.ShowMainWindowMessage)
            {
                ShowFromTray();
                return;
            }
            base.WndProc(ref m);
        }

        private void SaveSettings()
        {
            try
            {
                settings.Save();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                SetStatus("保存设置失败：" + ex.Message, StatusKind.Error);
            }
        }

        /// <summary>
        /// 应用现代化 UI 主题
        /// </summary>
        private void ApplyModernTheme()
        {
            UITheme.ApplyTheme(this);
            bool isAdministrator = Elevation.IsAdministrator();
            Text = AppTitle;
            UITheme.SetupTitleBar(pnlTitle, this, isAdministrator ? AppTitle + "（管理员）" : AppTitle);

            // 提权提示条只在普通权限时显示，管理员身份下隐藏并缩短窗体
            pnlElevation.BackColor = UITheme.WarningBackground;
            lblElevation.ForeColor = UITheme.WarningText;
            btnRestartAsAdmin.FlatStyle = FlatStyle.Flat;
            btnRestartAsAdmin.FlatAppearance.BorderSize = 0;
            btnRestartAsAdmin.FlatAppearance.MouseOverBackColor = Color.FromArgb(253, 230, 138);
            btnRestartAsAdmin.ForeColor = UITheme.WarningText;
            btnRestartAsAdmin.Font = new Font(UITheme.LabelFont, FontStyle.Bold);
            btnRestartAsAdmin.Cursor = Cursors.Hand;
            if (isAdministrator)
            {
                Height -= pnlElevation.Height;
                pnlElevation.Visible = false;
            }

            pnlAdapter.BackColor = UITheme.CardBackground;
            pnlStatus.BackColor = UITheme.CardBackground;
            pnlSeparator.BackColor = UITheme.Border;
            lblAdapterStatus.ForeColor = UITheme.TextSecondary;
            lblStatus.ForeColor = UITheme.TextSecondary;

            foreach (Label label in new[] { lblAdapter, lblIpMode, lblIpAddress, lblMask, lblGateway, lblDnsMode, lblDnsMain, lblDnsBackup, lblMacCaption, lblDhcpCaption, lblIPv6Caption })
                UITheme.StyleLabel(label);
            UITheme.StyleComboBox(cbxNetworkAdapter);
            foreach (TextBox box in EditableFields)
                UITheme.StyleTextBox(box);
            foreach (TextBox box in new[] { txtMac, txtDhcpServer, txtIPv6 })
                UITheme.StyleReadOnlyField(box);
            foreach (ButtonBase choice in new ButtonBase[] { rbIpDhcp, rbIpStatic, rbDnsAuto, rbDnsManual, chkShowAll })
                UITheme.StyleChoice(choice);
            foreach (Button button in new[] { btnRefresh, btnToggleAdapter, btnMore, btnDnsPreset, btnDnsBenchmark, btnProfile, btnDiagnose, btnReport, btnRevert })
                UITheme.StyleSecondaryButton(button);
            UITheme.StylePrimaryButton(btnApply);

            UITheme.SetCueBanner(txtIpAddress, "如 192.168.1.10，也可输入 192.168.1.10/24");
            UITheme.SetCueBanner(txtMask, "如 255.255.255.0");
            UITheme.SetCueBanner(txtGateway, "可留空");
            UITheme.SetCueBanner(txtDnsMain, "如 223.5.5.5");
            UITheme.SetCueBanner(txtDnsBackup, "可留空");

            toolTip.SetToolTip(btnRefresh, "刷新网卡列表");
            toolTip.SetToolTip(chkShowAll, "同时显示虚拟网卡（Hyper-V、VPN、蓝牙等）");
            toolTip.SetToolTip(btnDnsPreset, "选择常用公共 DNS");
            toolTip.SetToolTip(btnDnsBenchmark, "测试各公共 DNS 的响应速度并选择");
            toolTip.SetToolTip(btnRevert, "恢复为网卡当前的配置（Esc）");
            toolTip.SetToolTip(btnApply, "把以上配置应用到所选网卡（Enter）");

            foreach (TextBox box in new[] { txtMask, txtGateway, txtDnsMain, txtDnsBackup })
                box.Leave += Field_Leave;
        }

        #endregion

        #region 网卡列表

        /// <summary>
        /// 在后台读取网卡列表，并按网卡ID选回之前选中的网卡
        /// </summary>
        private async Task RefreshAdaptersAsync()
        {
            if (isRefreshing) return;
            isRefreshing = true;
            UpdateControlStates();
            try
            {
                bool includeAll = chkShowAll.Checked;
                List<NetworkAdapter> list = await Task.Run(() => AdapterService.GetAdapters(includeAll));
                if (IsDisposed) return;
                BindAdapterList(list);
            }
            catch (Exception ex) when (ex is NetworkInformationException || ex is System.Management.ManagementException)
            {
                SetStatus("读取网卡信息失败：" + ex.Message, StatusKind.Error);
            }
            finally
            {
                isRefreshing = false;
                if (!IsDisposed) UpdateControlStates();
            }
        }

        private void BindAdapterList(List<NetworkAdapter> list)
        {
            string targetId = selectedAdapterId;
            isBinding = true;
            try
            {
                cbxNetworkAdapter.DisplayMember = nameof(NetworkAdapter.DisplayName);
                cbxNetworkAdapter.DataSource = list;

                int index = list.FindIndex(a => string.Equals(a.NetworkInterfaceID, targetId, StringComparison.OrdinalIgnoreCase));
                if (index < 0 && list.Count > 0) index = 0;
                cbxNetworkAdapter.SelectedIndex = index;
            }
            finally
            {
                isBinding = false;
            }

            NetworkAdapter adapter = SelectedAdapter;
            selectedAdapterId = adapter == null ? null : adapter.NetworkInterfaceID;
            bool sameAdapter = adapter != null && string.Equals(adapter.NetworkInterfaceID, targetId, StringComparison.OrdinalIgnoreCase);
            ShowAdapter(adapter, !sameAdapter);

            if (list.Count == 0)
            {
                SetStatus(chkShowAll.Checked ? "没有找到网卡" : "没有找到物理网卡，可勾选“显示全部”查看虚拟网卡", StatusKind.Error);
            }
            else if (!sameAdapter && (adapter.ReadOnlyReason ?? adapter.ConfigNote) == null)
            {
                SetStatus("");
            }
        }

        private void cbxNetworkAdapter_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (isBinding) return;

            NetworkAdapter adapter = SelectedAdapter;
            selectedAdapterId = adapter == null ? null : adapter.NetworkInterfaceID;
            SetStatus("");
            ShowAdapter(adapter, true);
        }

        /// <summary>
        /// 显示网卡当前配置；adapter 为 null 时清空。announce 为 true 时在状态栏说明网卡的限制
        /// </summary>
        private void ShowAdapter(NetworkAdapter adapter, bool announce)
        {
            isLoadingFields = true;
            try
            {
                errorProvider.Clear();
                bool has = adapter != null;
                txtIpAddress.Text = has ? adapter.IpAddress : "";
                txtMask.Text = has ? adapter.SubnetMask : "";
                txtGateway.Text = has ? adapter.Gateway : "";
                txtDnsMain.Text = has ? adapter.DnsMain : "";
                txtDnsBackup.Text = has ? adapter.DnsBackup : "";
                txtMac.Text = has ? adapter.MacAddressText : "";
                txtDhcpServer.Text = has && adapter.IsDhcpEnabled && adapter.DhcpServer.Length > 0 ? adapter.DhcpServer : "-";
                List<string> ipv6 = has ? adapter.IPv6Addresses : new List<string>();
                txtIPv6.Text = ipv6.Count > 0 ? string.Join(Environment.NewLine, ipv6) : "-";

                bool dhcp = !has || adapter.IsDhcpEnabled;
                rbIpDhcp.Checked = dhcp;
                rbIpStatic.Checked = !dhcp;
                bool manualDns = !dhcp || (has && adapter.HasStaticDns);
                rbDnsManual.Checked = manualDns;
                rbDnsAuto.Checked = !manualDns;

                lblAdapterStatus.Text = !has ? "" : adapter.StatusText + (adapter.CanConfigure ? "" : " · 不可修改");
                toolTip.SetToolTip(lblAdapterStatus, !has ? "" : adapter.ReadOnlyReason ?? adapter.ConfigNote ?? adapter.DisplayName);
                btnToggleAdapter.Text = has && adapter.Status == AdapterStatus.Disabled ? "启用" : "禁用";

                isDirty = false;
            }
            finally
            {
                isLoadingFields = false;
            }

            if (announce && adapter != null && (adapter.ReadOnlyReason ?? adapter.ConfigNote) != null)
                SetStatus(adapter.ReadOnlyReason ?? adapter.ConfigNote);
            UpdateControlStates();
        }

        /// <summary>
        /// 根据当前模式和状态启用或禁用各控件
        /// </summary>
        private void UpdateControlStates()
        {
            NetworkAdapter adapter = SelectedAdapter;
            bool idle = !isBusy && !isRefreshing;
            bool editable = idle && adapter != null && adapter.CanConfigure;
            // 未连接的网卡无法可靠设置静态 IP（见 AdapterService.StaticOnDisconnectedReason）
            bool allowStatic = editable && adapter.ConfigBackend == BackendKind.Wmi;
            bool isStatic = rbIpStatic.Checked;
            bool manualDns = rbDnsManual.Checked;

            rbIpDhcp.Enabled = editable;
            rbIpStatic.Enabled = allowStatic;
            txtIpAddress.Enabled = allowStatic && isStatic;
            txtMask.Enabled = allowStatic && isStatic;
            txtGateway.Enabled = allowStatic && isStatic;

            // 静态 IP 时 DNS 只能手动设置
            rbDnsAuto.Enabled = editable && !isStatic;
            rbDnsManual.Enabled = editable;
            txtDnsMain.Enabled = editable && manualDns;
            txtDnsBackup.Enabled = editable && manualDns;
            btnDnsPreset.Enabled = editable;
            btnDnsBenchmark.Enabled = idle;

            // 不可编辑的输入框用灰底区分，否则白底看起来像能输入
            foreach (TextBox box in EditableFields)
                box.BackColor = box.Enabled ? UITheme.InputBackground : UITheme.HoverBackground;

            btnApply.Enabled = editable && isDirty;
            btnRevert.Enabled = idle && isDirty;
            cbxNetworkAdapter.Enabled = idle;
            chkShowAll.Enabled = idle;
            btnRefresh.Enabled = idle;
            btnToggleAdapter.Enabled = idle && adapter != null;
            btnMore.Enabled = idle;
            btnProfile.Enabled = idle;
        }

        private async void btnRefresh_Click(object sender, EventArgs e)
        {
            if (isDirty && !UITheme.Confirm("刷新会放弃尚未应用的修改，确定刷新？", "刷新")) return;
            SetStatus("");
            await RefreshAdaptersAsync();
        }

        private async void chkShowAll_CheckedChanged(object sender, EventArgs e)
        {
            if (isInitializing) return;
            settings.ShowAllAdapters = chkShowAll.Checked;
            SaveSettings();
            await RefreshAdaptersAsync();
        }

        #endregion

        #region 表单编辑与校验

        private void Field_TextChanged(object sender, EventArgs e)
        {
            if (isLoadingFields) return;
            errorProvider.SetError((Control)sender, "");
            MarkDirty();
        }

        private void IpMode_CheckedChanged(object sender, EventArgs e)
        {
            // 同组另一个按钮取消选中时也会触发，只处理被选中的那个
            if (!((RadioButton)sender).Checked) return;
            if (rbIpStatic.Checked && !rbDnsManual.Checked) rbDnsManual.Checked = true; // 静态 IP 时 DNS 只能手动设置
            OnModeChanged();
        }

        private void DnsMode_CheckedChanged(object sender, EventArgs e)
        {
            if (!((RadioButton)sender).Checked) return;
            OnModeChanged();
        }

        private void OnModeChanged()
        {
            if (isLoadingFields) return;
            errorProvider.Clear();
            MarkDirty();
        }

        private void MarkDirty()
        {
            isDirty = true;
            UpdateControlStates();
            SetStatus("有未应用的修改：按 Enter 应用，Esc 还原");
        }

        private void txtIpAddress_Leave(object sender, EventArgs e)
        {
            if (!txtIpAddress.Enabled) return;
            NormalizeCidrInput();

            // 只填了 IP 时先给出最常用的掩码，用户可以再改
            IPAddress ip;
            if (txtMask.Text.Trim().Length == 0 && IpValidator.TryParseIPv4(txtIpAddress.Text, out ip))
                txtMask.Text = "255.255.255.0";
            Field_Leave(sender, e);
        }

        /// <summary>
        /// 把“192.168.1.10/24”拆成 IP 地址和子网掩码
        /// </summary>
        private void NormalizeCidrInput()
        {
            IPAddress ip;
            int prefix;
            if (IpValidator.TryParseCidr(txtIpAddress.Text, out ip, out prefix))
            {
                txtIpAddress.Text = ip.ToString();
                txtMask.Text = IpValidator.PrefixToMask(prefix).ToString();
            }
        }

        /// <summary>
        /// 离开输入框时检查格式，网关是否同网段等跨字段规则在应用时统一检查
        /// </summary>
        private void Field_Leave(object sender, EventArgs e)
        {
            var box = (TextBox)sender;
            if (!box.Enabled) return;

            string text = box.Text.Trim();
            string error = "";
            IPAddress address;
            if (text.Length > 0)
            {
                if (!IpValidator.TryParseIPv4(text, out address))
                    error = "格式不正确，应为 4 段 0–255 的数字";
                else if (box == txtMask && IpValidator.MaskToPrefix(address) < 1)
                    error = "子网掩码无效，应为连续的 1 加连续的 0，如 255.255.255.0";
            }
            errorProvider.SetError(box, error);
        }

        private IpConfigRequest BuildRequest()
        {
            string[] dns = IpConfigRequest.DnsList(txtDnsMain.Text, txtDnsBackup.Text);
            if (rbIpDhcp.Checked)
            {
                return new IpConfigRequest
                {
                    UseDhcp = true,
                    UseDhcpDns = rbDnsAuto.Checked,
                    DnsServers = rbDnsAuto.Checked ? new string[0] : dns
                };
            }
            return IpConfigRequest.Static(txtIpAddress.Text.Trim(), txtMask.Text.Trim(), txtGateway.Text.Trim(), dns);
        }

        /// <summary>
        /// 把一份配置填入表单，作为尚未应用的修改
        /// </summary>
        private void ShowRequest(IpConfigRequest request)
        {
            isLoadingFields = true;
            try
            {
                errorProvider.Clear();
                rbIpDhcp.Checked = request.UseDhcp;
                rbIpStatic.Checked = !request.UseDhcp;
                bool autoDns = request.UseDhcp && request.UseDhcpDns;
                rbDnsAuto.Checked = autoDns;
                rbDnsManual.Checked = !autoDns;
                if (!request.UseDhcp)
                {
                    txtIpAddress.Text = request.IpAddress;
                    txtMask.Text = request.SubnetMask;
                    txtGateway.Text = request.Gateway;
                }
                if (!autoDns)
                {
                    txtDnsMain.Text = request.DnsServers.Length > 0 ? request.DnsServers[0] : "";
                    txtDnsBackup.Text = request.DnsServers.Length > 1 ? request.DnsServers[1] : "";
                }
            }
            finally
            {
                isLoadingFields = false;
            }
            MarkDirty();
        }

        private void ShowValidationErrors(List<ValidationError> errors)
        {
            errorProvider.Clear();
            foreach (ValidationError error in errors)
            {
                TextBox box = FieldOf(error.Field);
                if (errorProvider.GetError(box).Length == 0) errorProvider.SetError(box, error.Message);
            }
        }

        private TextBox FieldOf(IpField field)
        {
            switch (field)
            {
                case IpField.IpAddress: return txtIpAddress;
                case IpField.SubnetMask: return txtMask;
                case IpField.Gateway: return txtGateway;
                case IpField.DnsMain: return txtDnsMain;
                default: return txtDnsBackup;
            }
        }

        #endregion

        #region 应用配置

        private async void btnApply_Click(object sender, EventArgs e)
        {
            await ApplyFormAsync();
        }

        private async Task ApplyFormAsync()
        {
            NetworkAdapter adapter = SelectedAdapter;
            if (adapter == null || isBusy) return;
            if (!adapter.CanConfigure)
            {
                UITheme.ShowMessage(adapter.ReadOnlyReason, "无法修改", MessageBoxIcon.Warning);
                return;
            }

            NormalizeCidrInput();
            IpConfigRequest request = BuildRequest();
            List<ValidationError> errors = request.Validate();
            ShowValidationErrors(errors);
            if (errors.Count > 0)
            {
                SetStatus("请先修正标红的输入项：" + errors[0].Message, StatusKind.Error);
                FieldOf(errors[0].Field).Focus();
                return;
            }

            // 提权前先确认这次修改能做，免得白白弹出 UAC
            string unsupported = AdapterService.CheckSupported(adapter, request);
            if (unsupported != null)
            {
                UITheme.ShowMessage(unsupported, "无法应用", MessageBoxIcon.Warning);
                return;
            }

            if (!EnsureAdministrator()) return;
            if (IsPrimaryAdapter(adapter)
                && !UITheme.Confirm($"“{adapter.Name}”是当前上网使用的网卡，应用期间网络可能短暂中断。\n\n确定继续？", "修改上网网卡"))
                return;
            if (!await ConfirmNoIpConflictAsync(adapter, request)) return;

            ApplyResult result = await RunBusyAsync("正在应用配置…", () => AdapterService.Apply(adapter, request));
            if (IsDisposed) return;
            if (!result.Success)
            {
                SetStatus("✗ 应用失败", StatusKind.Error);
                UITheme.ShowMessage(result.Message, "应用失败", MessageBoxIcon.Error);
                return;
            }

            isDirty = false;
            UpdateControlStates();
            string note = adapter.ConfigNote == null ? "" : "。" + adapter.ConfigNote;
            SetStatus($"✓ 已应用到 {adapter.Name}（{DateTime.Now:HH:mm:ss}）{note}", StatusKind.Success);
            ScheduleRefresh(RefreshAfterApplyMilliseconds);
        }

        private void btnRevert_Click(object sender, EventArgs e)
        {
            if (isBusy) return;
            ShowAdapter(SelectedAdapter, false);
            SetStatus("已还原为网卡当前的配置");
        }

        /// <summary>
        /// 静态 IP 与本网段其他设备冲突时请用户确认；没有冲突或无法检测时返回 true
        /// </summary>
        private async Task<bool> ConfirmNoIpConflictAsync(NetworkAdapter adapter, IpConfigRequest request)
        {
            if (request.UseDhcp || adapter.Status != AdapterStatus.Connected
                || !IpConflict.ShouldProbe(adapter.IpAddress, adapter.SubnetMask, request.IpAddress))
                return true;

            SetBusy(true, "正在检查 IP 地址是否已被占用…");
            System.Net.NetworkInformation.PhysicalAddress owner;
            try
            {
                owner = await Task.Run(() => IpConflict.Probe(IPAddress.Parse(request.IpAddress)));
            }
            finally
            {
                if (!IsDisposed) SetBusy(false);
            }
            if (IsDisposed) return false;
            if (owner == null || owner.Equals(adapter.MacAddress))
            {
                SetStatus("");
                return true;
            }

            SetStatus($"IP 地址 {request.IpAddress} 已被占用", StatusKind.Error);
            return UITheme.Confirm($"IP 地址 {request.IpAddress} 已被 MAC 为 {NetworkAdapter.FormatMac(owner)} 的设备使用，"
                + "继续应用会造成 IP 冲突，两台设备都可能断网。\n\n确定继续？", "IP 地址冲突");
        }

        /// <summary>
        /// 是否为当前上网的网卡（默认路由所在网卡）
        /// </summary>
        private static bool IsPrimaryAdapter(NetworkAdapter adapter)
        {
            return adapter.InterfaceIndex >= 0 && adapter.InterfaceIndex == AdapterService.GetPrimaryInterfaceIndex();
        }

        /// <summary>
        /// 在后台执行耗时操作，期间禁用界面；意外异常转为失败结果，避免界面崩溃
        /// </summary>
        private async Task<ApplyResult> RunBusyAsync(string message, Func<ApplyResult> action)
        {
            SetBusy(true, message);
            try
            {
                return await Task.Run(action);
            }
            catch (Exception ex)
            {
                return ApplyResult.Fail(ex.Message);
            }
            finally
            {
                if (!IsDisposed) SetBusy(false);
            }
        }

        private void SetBusy(bool busy, string message = null)
        {
            isBusy = busy;
            UseWaitCursor = busy;
            btnApply.Text = busy ? "处理中…" : "应用";
            if (message != null) SetStatus(message);
            UpdateControlStates();
        }

        private void SetStatus(string text, StatusKind kind = StatusKind.Info)
        {
            lblStatus.Text = text ?? "";
            lblStatus.ForeColor = kind == StatusKind.Success ? UITheme.SuccessText
                : kind == StatusKind.Error ? UITheme.DangerText
                : UITheme.TextSecondary;
        }

        #endregion

        #region 网卡操作

        private async void btnToggleAdapter_Click(object sender, EventArgs e)
        {
            NetworkAdapter adapter = SelectedAdapter;
            if (adapter == null || isBusy) return;
            bool enable = adapter.Status == AdapterStatus.Disabled;
            if (!EnsureAdministrator()) return;
            if (!enable)
            {
                string warning = IsPrimaryAdapter(adapter) ? "\n\n这是当前上网使用的网卡，禁用后本机会断网，远程桌面也会断开。" : "";
                if (!UITheme.Confirm($"确定禁用网卡“{adapter.Name}”？{warning}", "禁用网卡")) return;
            }

            string action = enable ? "启用" : "禁用";
            ApplyResult result = await RunBusyAsync($"正在{action}网卡…", () => AdapterService.SetEnabled(adapter, enable));
            if (IsDisposed) return;
            if (!result.Success)
            {
                SetStatus($"✗ {action}网卡失败", StatusKind.Error);
                UITheme.ShowMessage(result.Message, action + "失败", MessageBoxIcon.Error);
                return;
            }

            SetStatus($"✓ 已{action}网卡 {adapter.Name}", StatusKind.Success);
            await RefreshAdaptersAsync();
        }

        private void btnMore_Click(object sender, EventArgs e)
        {
            NetworkAdapter adapter = SelectedAdapter;
            ClearMenu(moreMenu);

            var renew = new ToolStripMenuItem("重新获取 IP（续订 DHCP 租约）", null, async (s, ev) => await RenewDhcpAsync());
            renew.Enabled = adapter != null && adapter.IsDhcpEnabled && adapter.ConfigBackend == BackendKind.Wmi;
            moreMenu.Items.Add(renew);
            moreMenu.Items.Add(new ToolStripSeparator());
            moreMenu.Items.Add("启用列表中所有网卡", null, async (s, ev) => await SetAllAdaptersEnabledAsync(true));
            moreMenu.Items.Add("禁用列表中所有网卡", null, async (s, ev) => await SetAllAdaptersEnabledAsync(false));
            moreMenu.Items.Add(new ToolStripSeparator());
            moreMenu.Items.Add("打开系统“网络连接”", null, (s, ev) => OpenNetworkConnections());
            moreMenu.Items.Add(new ToolStripSeparator());
            AddSettingItems(moreMenu.Items);
            moreMenu.Show(btnMore, new Point(0, btnMore.Height));
        }

        private async Task RenewDhcpAsync()
        {
            NetworkAdapter adapter = SelectedAdapter;
            if (adapter == null || isBusy || !EnsureAdministrator()) return;

            ApplyResult result = await RunBusyAsync("正在重新获取 IP…", () => AdapterService.RenewDhcp(adapter));
            if (IsDisposed) return;
            if (!result.Success)
            {
                SetStatus("✗ 重新获取 IP 失败", StatusKind.Error);
                UITheme.ShowMessage(result.Message, "重新获取 IP 失败", MessageBoxIcon.Error);
                return;
            }
            SetStatus($"✓ 已为 {adapter.Name} 重新获取 IP", StatusKind.Success);
            ScheduleRefresh(RefreshAfterApplyMilliseconds);
        }

        /// <summary>
        /// 启用或禁用当前列表中的所有网卡
        /// </summary>
        private async Task SetAllAdaptersEnabledAsync(bool enable)
        {
            if (isBusy) return;
            string action = enable ? "启用" : "禁用";
            var listed = cbxNetworkAdapter.DataSource as List<NetworkAdapter> ?? new List<NetworkAdapter>();
            List<NetworkAdapter> targets = listed
                .Where(a => enable ? a.Status == AdapterStatus.Disabled : a.Status != AdapterStatus.Disabled)
                .ToList();
            if (targets.Count == 0)
            {
                SetStatus($"列表中没有需要{action}的网卡");
                return;
            }
            if (!EnsureAdministrator()) return;

            string names = string.Join("\n", targets.Select(a => "· " + a.Name));
            string warning = enable ? "" : "\n\n禁用后本机可能断网，远程桌面也会断开。";
            if (!UITheme.Confirm($"将{action}以下网卡：\n\n{names}{warning}\n\n确定继续？", action + "所有网卡")) return;

            var failures = new List<string>();
            await RunBusyAsync($"正在{action}网卡…", () =>
            {
                foreach (NetworkAdapter adapter in targets)
                {
                    ApplyResult result = AdapterService.SetEnabled(adapter, enable);
                    if (!result.Success) failures.Add(adapter.Name + "：" + result.Message);
                }
                return ApplyResult.Ok();
            });
            if (IsDisposed) return;

            if (failures.Count == 0)
            {
                SetStatus($"✓ 已{action} {targets.Count} 个网卡", StatusKind.Success);
            }
            else
            {
                SetStatus($"✗ 部分网卡{action}失败", StatusKind.Error);
                UITheme.ShowMessage(string.Join("\n", failures), action + "失败", MessageBoxIcon.Warning);
            }
            await RefreshAdaptersAsync();
        }

        private static void OpenNetworkConnections()
        {
            try
            {
                Process.Start(new ProcessStartInfo("ncpa.cpl") { UseShellExecute = true });
            }
            catch (Win32Exception)
            {
            }
        }

        /// <summary>
        /// 清空菜单项并释放，菜单每次打开前重新生成
        /// </summary>
        private static void ClearMenu(ContextMenuStrip menu)
        {
            while (menu.Items.Count > 0) menu.Items[0].Dispose();
        }

        #endregion

        #region DNS 预设

        private void btnDnsPreset_Click(object sender, EventArgs e)
        {
            ClearMenu(dnsPresetMenu);
            foreach (DnsCandidate preset in BuiltInAndCustomPresets())
            {
                string[] servers = preset.Servers;
                dnsPresetMenu.Items.Add(preset.Name + "    " + string.Join(" / ", servers), null, (s, ev) => ApplyDnsPreset(servers));
            }

            dnsPresetMenu.Items.Add(new ToolStripSeparator());
            dnsPresetMenu.Items.Add("测速并选择…", null, (s, ev) => OpenDnsBenchmark());
            string[] current = IpConfigRequest.DnsList(txtDnsMain.Text, txtDnsBackup.Text);
            var save = new ToolStripMenuItem("把当前 DNS 保存为预设…", null, (s, ev) => SaveDnsPreset(current))
            {
                Enabled = current.Length > 0 && IpValidator.ValidateDns(txtDnsMain.Text, txtDnsBackup.Text, true).Count == 0
            };
            dnsPresetMenu.Items.Add(save);

            if (settings.CustomDnsPresets.Count > 0)
            {
                var delete = new ToolStripMenuItem("删除自定义预设");
                foreach (DnsPreset p in settings.CustomDnsPresets.ToList())
                {
                    DnsPreset preset = p;
                    delete.DropDownItems.Add(preset.Name, null, (s, ev) =>
                    {
                        settings.CustomDnsPresets.Remove(preset);
                        SaveSettings();
                        SetStatus($"已删除 DNS 预设“{preset.Name}”");
                    });
                }
                dnsPresetMenu.Items.Add(delete);
            }
            dnsPresetMenu.Show(btnDnsPreset, new Point(0, btnDnsPreset.Height));
        }

        private void btnDnsBenchmark_Click(object sender, EventArgs e)
        {
            OpenDnsBenchmark();
        }

        /// <summary>
        /// 内置预设加自定义预设
        /// </summary>
        private List<DnsCandidate> BuiltInAndCustomPresets()
        {
            return ProfileManager.PresetDns.Select(p => new DnsCandidate(p.Key, p.Value))
                .Concat(settings.CustomDnsPresets.Select(p => new DnsCandidate(p.Name, p.Servers ?? new string[0])))
                .Where(c => c.Servers.Length > 0)
                .ToList();
        }

        private void OpenDnsBenchmark()
        {
            var candidates = new List<DnsCandidate>();
            NetworkAdapter adapter = SelectedAdapter;
            if (adapter != null)
            {
                var current = new DnsCandidate("当前 DNS", new[] { adapter.DnsMain, adapter.DnsBackup });
                if (current.Servers.Length > 0) candidates.Add(current);
            }
            candidates.AddRange(BuiltInAndCustomPresets());

            using (var dialog = new FrmDnsBenchmark(candidates))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK && dialog.SelectedServers != null)
                {
                    if (!rbDnsManual.Enabled)
                    {
                        UITheme.ShowMessage("当前网卡不能修改 DNS", "DNS 测速", MessageBoxIcon.Warning);
                        return;
                    }
                    ApplyDnsPreset(dialog.SelectedServers);
                }
            }
        }

        private void SaveDnsPreset(string[] servers)
        {
            string name = InputDialog.Show(this, "保存 DNS 预设", "预设名称：", "");
            if (name == null) return;

            settings.CustomDnsPresets.RemoveAll(p => p.Name == name);
            settings.CustomDnsPresets.Add(new DnsPreset { Name = name, Servers = servers });
            SaveSettings();
            SetStatus($"✓ 已保存 DNS 预设“{name}”", StatusKind.Success);
        }

        private void ApplyDnsPreset(string[] servers)
        {
            if (!rbDnsManual.Checked) rbDnsManual.Checked = true;
            txtDnsMain.Text = servers.Length > 0 ? servers[0] : "";
            txtDnsBackup.Text = servers.Length > 1 ? servers[1] : "";
        }

        #endregion

        #region 管理员权限

        private void btnRestartAsAdmin_Click(object sender, EventArgs e)
        {
            RestartAsAdministrator();
        }

        /// <summary>
        /// 写操作前检查管理员权限；非管理员时询问是否以管理员身份重启。返回 false 表示中止当前操作
        /// </summary>
        private bool EnsureAdministrator()
        {
            if (Elevation.IsAdministrator()) return true;

            if (UITheme.Confirm("修改网络配置需要管理员权限。\n\n是否以管理员身份重新启动本程序？已填写的内容会自动带过去。", "需要管理员权限"))
            {
                RestartAsAdministrator();
            }
            return false;
        }

        /// <summary>
        /// 暂存未应用的修改并以管理员身份重启
        /// </summary>
        private void RestartAsAdministrator()
        {
            NetworkAdapter adapter = SelectedAdapter;
            if (isDirty && adapter != null)
            {
                try
                {
                    new PendingEdit
                    {
                        AdapterId = adapter.NetworkInterfaceID,
                        IsDhcp = rbIpDhcp.Checked,
                        ManualDns = rbDnsManual.Checked,
                        IpAddress = txtIpAddress.Text.Trim(),
                        SubnetMask = txtMask.Text.Trim(),
                        Gateway = txtGateway.Text.Trim(),
                        DnsMain = txtDnsMain.Text.Trim(),
                        DnsBackup = txtDnsBackup.Text.Trim()
                    }.Save();
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // 暂存失败不影响重启，只是不能回填
                }
            }
            else
            {
                PendingEdit.Clear();
            }

            string arguments = adapter == null ? "" : "--adapter " + adapter.NetworkInterfaceID;
            if (Elevation.StartElevated(arguments) == null)
            {
                PendingEdit.Clear(); // 用户取消了 UAC
                return;
            }
            ExitApplication();
        }

        /// <summary>
        /// 回填以管理员身份重启前未应用的修改
        /// </summary>
        private void RestorePendingEdit()
        {
            PendingEdit pending = PendingEdit.Take();
            NetworkAdapter adapter = SelectedAdapter;
            if (pending == null || adapter == null
                || !string.Equals(pending.AdapterId, adapter.NetworkInterfaceID, StringComparison.OrdinalIgnoreCase))
                return;

            string[] dns = IpConfigRequest.DnsList(pending.DnsMain, pending.DnsBackup);
            ShowRequest(pending.IsDhcp
                ? new IpConfigRequest { UseDhcp = true, UseDhcpDns = !pending.ManualDns, DnsServers = pending.ManualDns ? dns : new string[0] }
                : IpConfigRequest.Static(pending.IpAddress, pending.SubnetMask, pending.Gateway, dns));
            SetStatus("已恢复提权前填写的内容，确认无误后点“应用”");
        }

        #endregion

        #region 配置方案

        private void btnProfile_Click(object sender, EventArgs e)
        {
            ClearMenu(profileMenu);

            // 已保存的配置方案，点击即载入
            foreach (IpProfile p in ProfileManager.Profiles)
            {
                IpProfile profile = p;
                profileMenu.Items.Add(profile.Name + "    " + profile.Summary, null, async (s, ev) => await ApplyProfileAsync(profile));
            }
            if (profileMenu.Items.Count > 0) profileMenu.Items.Add(new ToolStripSeparator());

            profileMenu.Items.Add("保存当前配置为方案…", null, (s, ev) => SaveCurrentProfile());
            profileMenu.Items.Add("管理配置方案…", null, async (s, ev) => await OpenProfileManagerAsync());
            profileMenu.Show(btnProfile, new Point(0, btnProfile.Height));
            ShowProfileLoadWarning();
        }

        /// <summary>
        /// 配置方案文件损坏或读取失败时提示一次
        /// </summary>
        private void ShowProfileLoadWarning()
        {
            string warning = ProfileManager.LoadWarning;
            if (warning == null || profileWarningShown) return;
            profileWarningShown = true;
            UITheme.ShowMessage(warning, "配置方案", MessageBoxIcon.Warning);
        }

        private async Task OpenProfileManagerAsync()
        {
            IpProfile toApply;
            using (var dialog = new FrmProfiles(cbxNetworkAdapter.DataSource as List<NetworkAdapter>))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                toApply = dialog.ProfileToApply;
            }
            if (toApply != null) await ApplyProfileAsync(toApply);
        }

        /// <summary>
        /// 保存当前表单为配置方案
        /// </summary>
        private void SaveCurrentProfile()
        {
            NetworkAdapter adapter = SelectedAdapter;
            string name = InputDialog.Show(this, "保存配置方案", "方案名称：", adapter == null ? "" : adapter.Name);
            if (string.IsNullOrWhiteSpace(name)) return;

            bool isDhcp = rbIpDhcp.Checked;
            bool manualDns = rbDnsManual.Checked;
            var profile = new IpProfile
            {
                Name = name,
                IsDhcp = isDhcp,
                IpAddress = isDhcp ? "" : txtIpAddress.Text.Trim(),
                SubnetMask = isDhcp ? "" : txtMask.Text.Trim(),
                Gateway = isDhcp ? "" : txtGateway.Text.Trim(),
                ManualDns = manualDns,
                DnsMain = manualDns ? txtDnsMain.Text.Trim() : "",
                DnsBackup = manualDns ? txtDnsBackup.Text.Trim() : ""
            };

            List<ValidationError> errors = profile.ToRequest().Validate();
            if (errors.Count > 0)
            {
                UITheme.ShowMessage("当前配置有误，无法保存：" + errors[0].Message, "保存配置", MessageBoxIcon.Warning);
                return;
            }
            if (ProfileManager.Find(name) != null
                && !UITheme.Confirm($"已存在名为“{name}”的配置方案，是否覆盖？", "保存配置"))
                return;

            try
            {
                ProfileManager.Add(profile);
                SetStatus($"✓ 配置方案“{name}”已保存", StatusKind.Success);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                UITheme.ShowMessage("保存失败：" + ex.Message, "保存配置", MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 载入配置方案到表单，并询问是否立即应用。方案绑定了网卡时先切换到该网卡。
        /// </summary>
        private async Task ApplyProfileAsync(IpProfile profile)
        {
            if (isBusy) return;

            if (!string.IsNullOrEmpty(profile.AdapterMac))
            {
                var listed = cbxNetworkAdapter.DataSource as List<NetworkAdapter> ?? new List<NetworkAdapter>();
                NetworkAdapter bound = listed.FirstOrDefault(a => a.MacAddressText == profile.AdapterMac);
                if (bound == null)
                {
                    if (!UITheme.Confirm($"方案“{profile.Name}”绑定的网卡（{profile.AdapterMac}）不在列表中。\n\n是否应用到当前选中的网卡？", "应用配置方案"))
                        return;
                }
                else if (bound != SelectedAdapter)
                {
                    cbxNetworkAdapter.SelectedItem = bound;
                }
            }

            NetworkAdapter adapter = SelectedAdapter;
            if (adapter == null) return;

            ShowRequest(profile.ToRequest());
            if (UITheme.Confirm($"已载入配置方案“{profile.Name}”。\n\n是否立即应用到“{adapter.Name}”？", "应用配置方案"))
                await ApplyFormAsync();
        }

        #endregion

        #region 诊断与报表

        private void btnDiagnose_Click(object sender, EventArgs e)
        {
            new FrmDiagnosis(SelectedAdapter).Show(this);
        }

        private void btnReport_Click(object sender, EventArgs e)
        {
            new FrmInfo().Show(this);
        }

        #endregion

        #region 托盘

        private void CreateTrayIcon()
        {
            trayIcon = new NotifyIcon
            {
                Icon = UITheme.AppIcon,
                Text = AppTitle,
                ContextMenuStrip = trayMenu,
                Visible = true
            };
            trayIcon.MouseDoubleClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) ShowFromTray();
            };
            trayMenu.Opening += (s, e) => BuildTrayMenu();
            FormClosing += FrmMain_FormClosing;
        }

        private void BuildTrayMenu()
        {
            ClearMenu(trayMenu);
            trayMenu.Items.Add("显示主界面", null, (s, e) => ShowFromTray());
            trayMenu.Items.Add(new ToolStripSeparator());

            // 配置方案：一键载入并确认应用
            foreach (IpProfile p in ProfileManager.Profiles)
            {
                IpProfile profile = p;
                trayMenu.Items.Add(profile.Name + "    " + profile.Summary, null, async (s, e) =>
                {
                    ShowFromTray();
                    await ApplyProfileAsync(profile);
                });
            }
            trayMenu.Items.Add("管理配置方案…", null, async (s, e) =>
            {
                ShowFromTray();
                await OpenProfileManagerAsync();
            });

            trayMenu.Items.Add(new ToolStripSeparator());
            AddSettingItems(trayMenu.Items);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("退出", null, (s, e) => ExitApplication());
        }

        /// <summary>
        /// 设置项，托盘菜单和“更多”菜单共用
        /// </summary>
        private void AddSettingItems(ToolStripItemCollection items)
        {
            var tray = new ToolStripMenuItem("关闭时最小化到托盘") { Checked = settings.MinimizeToTray };
            tray.Click += (s, e) =>
            {
                settings.MinimizeToTray = !settings.MinimizeToTray;
                SaveSettings();
            };
            var autoStart = new ToolStripMenuItem("开机自动启动（只显示托盘图标）") { Checked = AutoStart.IsEnabled };
            autoStart.Click += (s, e) => ToggleAutoStart();
            items.Add(tray);
            items.Add(autoStart);
        }

        private void ToggleAutoStart()
        {
            bool enable = !AutoStart.IsEnabled;
            try
            {
                AutoStart.Set(enable, Application.ExecutablePath);
                SetStatus(enable ? "✓ 已设置开机自动启动" : "已取消开机自动启动", enable ? StatusKind.Success : StatusKind.Info);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is IOException)
            {
                UITheme.ShowMessage("设置开机自动启动失败：" + ex.Message, "开机自动启动", MessageBoxIcon.Error);
            }
        }

        private void HideToTray()
        {
            Hide();
            if (!settings.TrayHintShown)
            {
                trayIcon.ShowBalloonTip(3000, AppTitle, "程序仍在后台运行，双击托盘图标可重新打开", ToolTipIcon.Info);
                settings.TrayHintShown = true;
                SaveSettings();
            }
        }

        private void ShowFromTray()
        {
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            ShowInTaskbar = true;
            Activate();
            BringToFront();
        }

        /// <summary>
        /// 真正退出程序（不隐藏到托盘）
        /// </summary>
        private void ExitApplication()
        {
            exitRequested = true;
            Close();
        }

        #endregion

        #region 网络变化自动刷新

        private void NetworkChange_NetworkAddressChanged(object sender, EventArgs e)
        {
            OnNetworkChanged();
        }

        private void NetworkChange_NetworkAvailabilityChanged(object sender, NetworkAvailabilityEventArgs e)
        {
            OnNetworkChanged();
        }

        /// <summary>
        /// 系统网络事件在线程池线程上触发，转到界面线程后防抖刷新
        /// </summary>
        private void OnNetworkChanged()
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke((Action)(() => ScheduleRefresh(RefreshDebounceMilliseconds)));
            }
            catch (InvalidOperationException)
            {
                // 窗体正在关闭
            }
        }

        private void ScheduleRefresh(int delayMilliseconds)
        {
            refreshTimer.Stop();
            refreshTimer.Interval = delayMilliseconds;
            refreshTimer.Start();
        }

        private async void RefreshTimer_Tick(object sender, EventArgs e)
        {
            refreshTimer.Stop();
            if (isBusy)
            {
                ScheduleRefresh(RefreshDebounceMilliseconds);
                return;
            }
            if (isDirty)
            {
                SetStatus("网络配置已变化，点击 ⟳ 查看最新配置（会放弃未应用的修改）");
                return;
            }
            await RefreshAdaptersAsync();
        }

        #endregion
    }
}
