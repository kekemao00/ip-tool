using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows.Forms;
using IP_UpdateTest.Core;
using IP_UpdateTest.Ui;

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
        private readonly ContextMenuStrip dnsPresetMenu = Menus.Create();
        private readonly ContextMenuStrip moreMenu = Menus.Create();
        private readonly ContextMenuStrip profileMenu = Menus.Create();
        private readonly ContextMenuStrip trayMenu = Menus.Create();
        private readonly AppSettings settings;
        private NotifyIcon trayIcon;

        /// <summary>
        /// 当前列表中的网卡
        /// </summary>
        private List<NetworkAdapter> adapters = new List<NetworkAdapter>();

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
            : this(initialAdapterId, startInTray, AppSettings.Load(), Elevation.IsAdministrator())
        {
        }

        /// <summary>
        /// 可注入设置和权限（界面截图测试用）
        /// </summary>
        internal FrmMain(string initialAdapterId, bool startInTray, AppSettings settings, bool isAdministrator)
        {
            this.settings = settings;
            StartPosition = FormStartPosition.CenterScreen;
            Text = AppTitle;
            TitleBar.Text = "IP Tool";
            TitleBar.Subtitle = AppTitle;
            TitleBar.ShowSearch = true;
            TitleBar.SearchClicked += (s, e) => OpenCommandPalette();
            showElevation = !isAdministrator;
            if (isAdministrator) TitleBar.Chip = "管理员";

            BuildControls();
            WireEvents();
            ApplyHints();
            UpdateWindowHeight();

            selectedAdapterId = initialAdapterId;
            this.startInTray = startInTray;
            refreshTimer.Tick += RefreshTimer_Tick;
            Load += FrmMain_Load;
            FormClosed += FrmMain_FormClosed;
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

        private FieldBox[] EditableFields
        {
            get { return new[] { txtIpAddress, txtMask, txtGateway, txtDnsMain, txtDnsBackup }; }
        }

        private bool IsStatic
        {
            get { return segIpMode.SelectedIndex == 1; }
        }

        private bool IsManualDns
        {
            get { return segDnsMode.SelectedIndex == 1; }
        }

        #region 加载与外观

        private void WireEvents()
        {
            btnRestartAsAdmin.Click += btnRestartAsAdmin_Click;
            cbxNetworkAdapter.SelectedIndexChanged += cbxNetworkAdapter_SelectedIndexChanged;
            btnRefresh.Click += btnRefresh_Click;
            chkShowAll.CheckedChanged += chkShowAll_CheckedChanged;
            btnToggleAdapter.Click += btnToggleAdapter_Click;
            btnMore.Click += btnMore_Click;
            segIpMode.SelectedIndexChanged += IpMode_Changed;
            segDnsMode.SelectedIndexChanged += DnsMode_Changed;
            btnDnsPreset.Click += btnDnsPreset_Click;
            btnDnsBenchmark.Click += btnDnsBenchmark_Click;
            foreach (FieldBox box in EditableFields) box.TextChanged += Field_TextChanged;
            txtIpAddress.Box.Leave += txtIpAddress_Leave;
            foreach (FieldBox box in new[] { txtMask, txtGateway, txtDnsMain, txtDnsBackup })
            {
                FieldBox field = box;
                field.Box.Leave += (s, e) => ValidateField(field);
            }
            btnProfile.Click += btnProfile_Click;
            btnDiagnose.Click += btnDiagnose_Click;
            btnReport.Click += btnReport_Click;
            btnRevert.Click += btnRevert_Click;
            btnApply.Click += btnApply_Click;

            cbxNetworkAdapter.Format = o => ((NetworkAdapter)o).Name;
            cbxNetworkAdapter.Detail = o => ((NetworkAdapter)o).StatusText;
            cbxNetworkAdapter.ItemGlyph = o => AdapterGlyph((NetworkAdapter)o);
        }

        private static Glyph AdapterGlyph(NetworkAdapter adapter)
        {
            return adapter != null && (adapter.NetworkInterfaceType ?? "").IndexOf("Wireless", StringComparison.OrdinalIgnoreCase) >= 0
                ? Glyph.Wifi
                : Glyph.Network;
        }

        private void ApplyHints()
        {
            txtIpAddress.Placeholder = "如 192.168.1.10，也可输入 192.168.1.10/24";
            txtMask.Placeholder = "如 255.255.255.0";
            txtGateway.Placeholder = "可留空";
            txtDnsMain.Placeholder = "如 223.5.5.5";
            txtDnsBackup.Placeholder = "可留空";

            InkToolTip tips = InkToolTip.Shared;
            tips.SetToolTip(btnRefresh, "刷新网卡列表");
            tips.SetToolTip(chkShowAll, "同时显示虚拟网卡（Hyper-V、VPN、蓝牙等）");
            tips.SetToolTip(btnDnsPreset, "选择常用公共 DNS");
            tips.SetToolTip(btnDnsBenchmark, "测试各公共 DNS 的响应速度并选择");
            tips.SetToolTip(btnDiagnose, "分层检测网络：网卡、路由器、外网、DNS、代理 / VPN、UDP");
            tips.SetToolTip(btnReport, "网卡信息报表，可复制或导出");
            tips.SetToolTip(btnRevert, "恢复为网卡当前的配置（Esc）");
            tips.SetToolTip(btnApply, "把以上配置应用到所选网卡（Enter）");
        }

        /// <summary>
        /// 界面截图测试：启动时不读网卡、不建托盘图标
        /// </summary>
        internal bool SnapshotMode { get; set; }

        private async void FrmMain_Load(object sender, EventArgs e)
        {
            if (SnapshotMode)
            {
                isInitializing = false;
                return;
            }
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

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.K) && !HasSheet)
            {
                OpenCommandPalette();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
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

        internal void BindAdapterList(List<NetworkAdapter> list)
        {
            string targetId = selectedAdapterId;
            isBinding = true;
            try
            {
                adapters = list;
                int index = list.FindIndex(a => string.Equals(a.NetworkInterfaceID, targetId, StringComparison.OrdinalIgnoreCase));
                if (index < 0 && list.Count > 0) index = 0;
                cbxNetworkAdapter.SetItems(list.Cast<object>(), index);
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
                SetStatus(chkShowAll.Checked ? "没有找到网卡" : "没有找到物理网卡，可打开“显示全部”查看虚拟网卡", StatusKind.Error);
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
        /// 显示网卡当前配置；adapter 为 null 时清空。announce 为 true 时在状态行说明网卡的限制
        /// </summary>
        private void ShowAdapter(NetworkAdapter adapter, bool announce)
        {
            isLoadingFields = true;
            try
            {
                ClearErrors();
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
                segIpMode.SelectedIndex = dhcp ? 0 : 1;
                bool manualDns = !dhcp || (has && adapter.HasStaticDns);
                segDnsMode.SelectedIndex = manualDns ? 1 : 0;

                lblAdapterDescription.Text = has ? adapter.InterfaceDescription ?? "" : "";
                lblAdapterStatus.Glyph = has ? AdapterGlyph(adapter) : Glyph.None;
                lblAdapterStatus.Text = !has ? "" : adapter.StatusText + (adapter.CanConfigure ? "" : " · 不可修改");
                InkToolTip.Shared.SetToolTip(lblAdapterStatus, !has ? "" : adapter.ReadOnlyReason ?? adapter.ConfigNote ?? adapter.DisplayName);
                bool disabled = has && adapter.Status == AdapterStatus.Disabled;
                btnToggleAdapter.Text = disabled ? "启用" : "禁用";

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
            bool isStatic = IsStatic;
            bool manualDns = IsManualDns;

            segIpMode.Enabled = editable;
            segIpMode.SetItemEnabled(1, allowStatic || isStatic);
            txtIpAddress.Enabled = allowStatic && isStatic;
            txtMask.Enabled = allowStatic && isStatic;
            txtGateway.Enabled = allowStatic && isStatic;

            // 静态 IP 时 DNS 只能手动设置
            segDnsMode.Enabled = editable;
            segDnsMode.SetItemEnabled(0, !isStatic);
            txtDnsMain.Enabled = editable && manualDns;
            txtDnsBackup.Enabled = editable && manualDns;
            btnDnsPreset.Enabled = editable;
            btnDnsBenchmark.Enabled = idle;

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
            if (isDirty && !await ConfirmAsync(btnRefresh, "刷新", "刷新会放弃尚未应用的修改，确定刷新？", "刷新")) return;
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
            ((FieldBox)sender).SetError(null);
            MarkDirty();
        }

        private void IpMode_Changed(object sender, EventArgs e)
        {
            if (IsStatic && !IsManualDns) segDnsMode.SelectedIndex = 1; // 静态 IP 时 DNS 只能手动设置
            OnModeChanged();
        }

        private void DnsMode_Changed(object sender, EventArgs e)
        {
            OnModeChanged();
        }

        private void OnModeChanged()
        {
            if (isLoadingFields) return;
            ClearErrors();
            MarkDirty();
        }

        private void MarkDirty()
        {
            isDirty = true;
            UpdateControlStates();
            SetStatus("有未应用的修改：按 Enter 应用，Esc 还原");
        }

        private void ClearErrors()
        {
            foreach (FieldBox box in EditableFields) box.SetError(null, false);
        }

        private void txtIpAddress_Leave(object sender, EventArgs e)
        {
            if (!txtIpAddress.Enabled) return;
            NormalizeCidrInput();

            // 只填了 IP 时先给出最常用的掩码，用户可以再改
            IPAddress ip;
            if (txtMask.Text.Trim().Length == 0 && IpValidator.TryParseIPv4(txtIpAddress.Text, out ip))
                txtMask.Text = "255.255.255.0";
            ValidateField(txtIpAddress);
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
        private void ValidateField(FieldBox box)
        {
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
            if (error != box.ErrorText) box.SetError(error);
        }

        private IpConfigRequest BuildRequest()
        {
            string[] dns = IpConfigRequest.DnsList(txtDnsMain.Text, txtDnsBackup.Text);
            if (!IsStatic)
            {
                return new IpConfigRequest
                {
                    UseDhcp = true,
                    UseDhcpDns = !IsManualDns,
                    DnsServers = !IsManualDns ? new string[0] : dns
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
                ClearErrors();
                segIpMode.SelectedIndex = request.UseDhcp ? 0 : 1;
                bool autoDns = request.UseDhcp && request.UseDhcpDns;
                segDnsMode.SelectedIndex = autoDns ? 0 : 1;
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
            ClearErrors();
            foreach (ValidationError error in errors)
            {
                FieldBox box = FieldOf(error.Field);
                if (box.ErrorText.Length == 0) box.SetError(error.Message);
            }
        }

        private FieldBox FieldOf(IpField field)
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
                await AlertAsync(btnApply, "无法修改", adapter.ReadOnlyReason);
                return;
            }

            NormalizeCidrInput();
            IpConfigRequest request = BuildRequest();
            List<ValidationError> errors = request.Validate();
            ShowValidationErrors(errors);
            if (errors.Count > 0)
            {
                SetStatus("请先修正标红的输入项：" + errors[0].Message, StatusKind.Error);
                FieldOf(errors[0].Field).FocusInput();
                return;
            }

            // 提权前先确认这次修改能做，免得白白弹出 UAC
            string unsupported = AdapterService.CheckSupported(adapter, request);
            if (unsupported != null)
            {
                await AlertAsync(btnApply, "无法应用", unsupported);
                return;
            }

            if (!await EnsureAdministratorAsync(btnApply)) return;
            if (IsPrimaryAdapter(adapter)
                && !await ConfirmAsync(btnApply, "修改上网网卡", $"“{adapter.Name}”是当前上网使用的网卡，应用期间网络可能短暂中断。确定继续？", "继续应用"))
                return;
            if (!await ConfirmNoIpConflictAsync(adapter, request)) return;

            ApplyResult result = await RunBusyAsync("正在应用配置…", () => AdapterService.Apply(adapter, request), btnApply);
            if (IsDisposed) return;
            if (!result.Success)
            {
                btnApply.Fail("应用失败");
                SetStatus("应用失败：" + result.Message, StatusKind.Error);
                await AlertAsync(btnApply, "应用失败", result.Message);
                return;
            }

            btnApply.Succeed();
            isDirty = false;
            UpdateControlStates();
            string note = adapter.ConfigNote == null ? "" : "。" + adapter.ConfigNote;
            SetStatus($"已应用到 {adapter.Name}（{DateTime.Now:HH:mm:ss}）{note}", StatusKind.Success);
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
            btnApply.BeginLoading();
            PhysicalAddress owner;
            try
            {
                owner = await Task.Run(() => IpConflict.Probe(IPAddress.Parse(request.IpAddress)));
            }
            finally
            {
                if (!IsDisposed)
                {
                    SetBusy(false);
                    btnApply.Reset();
                }
            }
            if (IsDisposed) return false;
            if (owner == null || owner.Equals(adapter.MacAddress))
            {
                SetStatus("");
                return true;
            }

            SetStatus($"IP 地址 {request.IpAddress} 已被占用", StatusKind.Error);
            return await ConfirmAsync(btnApply, "IP 地址冲突", $"IP 地址 {request.IpAddress} 已被 MAC 为 {NetworkAdapter.FormatMac(owner)} 的设备使用，"
                + "继续应用会造成 IP 冲突，两台设备都可能断网。确定继续？", "仍然应用", true);
        }

        /// <summary>
        /// 是否为当前上网的网卡（默认路由所在网卡）
        /// </summary>
        private static bool IsPrimaryAdapter(NetworkAdapter adapter)
        {
            return adapter.InterfaceIndex >= 0 && adapter.InterfaceIndex == AdapterService.GetPrimaryInterfaceIndex();
        }

        /// <summary>
        /// 在后台执行耗时操作，期间禁用界面；意外异常转为失败结果，避免界面崩溃。
        /// morph 为发起操作的按钮时，它收成圆转圈
        /// </summary>
        private async Task<ApplyResult> RunBusyAsync(string message, Func<ApplyResult> action, SpringButton morph = null)
        {
            SetBusy(true, message);
            if (morph != null) morph.BeginLoading();
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
            if (message != null) SetStatus(message);
            UpdateControlStates();
        }

        /// <summary>
        /// 状态行显示持续性的说明；成功和失败另外在顶部通知
        /// </summary>
        private void SetStatus(string text, StatusKind kind = StatusKind.Info)
        {
            lblStatus.Text = text ?? "";
            lblStatus.TextColor = kind == StatusKind.Error ? Palette.Danger : kind == StatusKind.Success ? Palette.Ink2 : Palette.Ink3;
            if (kind != StatusKind.Info && !string.IsNullOrEmpty(text) && Visible)
                Notify(kind == StatusKind.Success ? NoticeKind.Success : NoticeKind.Error, text);
        }

        #endregion

        #region 网卡操作

        private async void btnToggleAdapter_Click(object sender, EventArgs e)
        {
            NetworkAdapter adapter = SelectedAdapter;
            if (adapter == null || isBusy) return;
            bool enable = adapter.Status == AdapterStatus.Disabled;
            if (!await EnsureAdministratorAsync(btnToggleAdapter)) return;
            if (!enable)
            {
                string warning = IsPrimaryAdapter(adapter) ? "这是当前上网使用的网卡，禁用后本机会断网，远程桌面也会断开。" : "";
                if (!await ConfirmAsync(btnToggleAdapter, "禁用网卡", $"确定禁用网卡“{adapter.Name}”？{warning}", "禁用", true)) return;
            }

            string action = enable ? "启用" : "禁用";
            ApplyResult result = await RunBusyAsync($"正在{action}网卡…", () => AdapterService.SetEnabled(adapter, enable), btnToggleAdapter);
            if (IsDisposed) return;
            if (!result.Success)
            {
                btnToggleAdapter.Fail(action + "失败");
                SetStatus($"{action}网卡失败", StatusKind.Error);
                await AlertAsync(btnToggleAdapter, action + "失败", result.Message);
                return;
            }

            btnToggleAdapter.Reset();
            SetStatus($"已{action}网卡 {adapter.Name}", StatusKind.Success);
            await RefreshAdaptersAsync();
        }

        private void btnMore_Click(object sender, EventArgs e)
        {
            NetworkAdapter adapter = SelectedAdapter;
            Menus.Clear(moreMenu);

            ToolStripMenuItem renew = Menus.Item("重新获取 IP（续订 DHCP 租约）", Glyph.Refresh, async (s, ev) => await RenewDhcpAsync());
            renew.Enabled = adapter != null && adapter.IsDhcpEnabled && adapter.ConfigBackend == BackendKind.Wmi;
            moreMenu.Items.Add(renew);
            moreMenu.Items.Add(new ToolStripSeparator());
            moreMenu.Items.Add(Menus.Item("启用列表中所有网卡", Glyph.Power, async (s, ev) => await SetAllAdaptersEnabledAsync(true)));
            moreMenu.Items.Add(Menus.Item("禁用列表中所有网卡", Glyph.Power, async (s, ev) => await SetAllAdaptersEnabledAsync(false), true));
            moreMenu.Items.Add(new ToolStripSeparator());
            moreMenu.Items.Add(Menus.Item("打开系统“网络连接”", Glyph.ExternalLink, (s, ev) => OpenNetworkConnections()));
            moreMenu.Items.Add(new ToolStripSeparator());
            AddSettingItems(moreMenu.Items);
            moreMenu.Show(btnMore, new System.Drawing.Point(0, btnMore.Height + P(4)));
        }

        private async Task RenewDhcpAsync()
        {
            NetworkAdapter adapter = SelectedAdapter;
            if (adapter == null || isBusy || !await EnsureAdministratorAsync(btnMore)) return;

            ApplyResult result = await RunBusyAsync("正在重新获取 IP…", () => AdapterService.RenewDhcp(adapter));
            if (IsDisposed) return;
            if (!result.Success)
            {
                SetStatus("重新获取 IP 失败", StatusKind.Error);
                await AlertAsync(btnMore, "重新获取 IP 失败", result.Message);
                return;
            }
            SetStatus($"已为 {adapter.Name} 重新获取 IP", StatusKind.Success);
            ScheduleRefresh(RefreshAfterApplyMilliseconds);
        }

        /// <summary>
        /// 启用或禁用当前列表中的所有网卡
        /// </summary>
        private async Task SetAllAdaptersEnabledAsync(bool enable)
        {
            if (isBusy) return;
            string action = enable ? "启用" : "禁用";
            List<NetworkAdapter> targets = adapters
                .Where(a => enable ? a.Status == AdapterStatus.Disabled : a.Status != AdapterStatus.Disabled)
                .ToList();
            if (targets.Count == 0)
            {
                SetStatus($"列表中没有需要{action}的网卡");
                return;
            }
            if (!await EnsureAdministratorAsync(btnMore)) return;

            string names = string.Join("、", targets.Select(a => a.Name));
            string warning = enable ? "" : "禁用后本机可能断网，远程桌面也会断开。";
            if (!await ConfirmAsync(btnMore, action + "所有网卡", $"将{action}以下网卡：{names}。{warning}确定继续？", action, !enable)) return;

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
                SetStatus($"已{action} {targets.Count} 个网卡", StatusKind.Success);
            }
            else
            {
                SetStatus($"部分网卡{action}失败", StatusKind.Error);
                await AlertAsync(btnMore, action + "失败", string.Join("\n", failures));
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

        #endregion

        #region DNS 预设

        private void btnDnsPreset_Click(object sender, EventArgs e)
        {
            Menus.Clear(dnsPresetMenu);
            foreach (DnsCandidate preset in BuiltInAndCustomPresets())
            {
                string[] servers = preset.Servers;
                dnsPresetMenu.Items.Add(Menus.Item(preset.Name, string.Join(" / ", servers), Glyph.Server, (s, ev) => ApplyDnsPreset(servers)));
            }

            dnsPresetMenu.Items.Add(new ToolStripSeparator());
            dnsPresetMenu.Items.Add(Menus.Item("测速并选择…", Glyph.Gauge, (s, ev) => OpenDnsBenchmark()));
            string[] current = IpConfigRequest.DnsList(txtDnsMain.Text, txtDnsBackup.Text);
            ToolStripMenuItem save = Menus.Item("把当前 DNS 保存为预设…", Glyph.Save, async (s, ev) => await SaveDnsPresetAsync(current));
            save.Enabled = current.Length > 0 && IpValidator.ValidateDns(txtDnsMain.Text, txtDnsBackup.Text, true).Count == 0;
            dnsPresetMenu.Items.Add(save);

            if (settings.CustomDnsPresets.Count > 0)
            {
                ToolStripMenuItem delete = Menus.Item("删除自定义预设", Glyph.Trash, null, true);
                foreach (DnsPreset p in settings.CustomDnsPresets.ToList())
                {
                    DnsPreset preset = p;
                    delete.DropDownItems.Add(Menus.Item(preset.Name, Glyph.Trash, (s, ev) =>
                    {
                        settings.CustomDnsPresets.Remove(preset);
                        SaveSettings();
                        SetStatus($"已删除 DNS 预设“{preset.Name}”");
                    }, true));
                }
                dnsPresetMenu.Items.Add(delete);
            }
            dnsPresetMenu.Show(btnDnsPreset, new System.Drawing.Point(0, btnDnsPreset.Height + P(4)));
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
                    if (!segDnsMode.Enabled)
                    {
                        Notify(NoticeKind.Warning, "当前网卡不能修改 DNS");
                        return;
                    }
                    ApplyDnsPreset(dialog.SelectedServers);
                }
            }
        }

        private async Task SaveDnsPresetAsync(string[] servers)
        {
            string name = await Sheets.PromptAsync(this, btnDnsPreset, "保存 DNS 预设", "预设名称", "", Glyph.Server);
            if (name == null) return;

            settings.CustomDnsPresets.RemoveAll(p => p.Name == name);
            settings.CustomDnsPresets.Add(new DnsPreset { Name = name, Servers = servers });
            SaveSettings();
            SetStatus($"已保存 DNS 预设“{name}”", StatusKind.Success);
        }

        private void ApplyDnsPreset(string[] servers)
        {
            if (!IsManualDns) segDnsMode.SelectedIndex = 1;
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
        private async Task<bool> EnsureAdministratorAsync(Control origin)
        {
            if (Elevation.IsAdministrator()) return true;

            if (await ConfirmAsync(origin, "需要管理员权限", "修改网络配置需要管理员权限。是否以管理员身份重新启动本程序？已填写的内容会自动带过去。", "以管理员身份重启"))
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
                        IsDhcp = !IsStatic,
                        ManualDns = IsManualDns,
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
            Menus.Clear(profileMenu);

            // 已保存的配置方案，点击即载入
            foreach (IpProfile p in ProfileManager.Profiles)
            {
                IpProfile profile = p;
                profileMenu.Items.Add(Menus.Item(profile.Name, profile.Summary, Glyph.Layers, async (s, ev) => await ApplyProfileAsync(profile, btnProfile)));
            }
            if (profileMenu.Items.Count > 0) profileMenu.Items.Add(new ToolStripSeparator());

            profileMenu.Items.Add(Menus.Item("保存当前配置为方案…", Glyph.Save, async (s, ev) => await SaveCurrentProfileAsync()));
            profileMenu.Items.Add(Menus.Item("管理配置方案…", Glyph.Pencil, async (s, ev) => await OpenProfileManagerAsync()));
            profileMenu.Show(btnProfile, new System.Drawing.Point(0, btnProfile.Height + P(4)));
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
            Notify(NoticeKind.Warning, warning);
        }

        private async Task OpenProfileManagerAsync()
        {
            IpProfile toApply;
            using (var dialog = new FrmProfiles(adapters))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                toApply = dialog.ProfileToApply;
            }
            if (toApply != null) await ApplyProfileAsync(toApply, btnProfile);
        }

        /// <summary>
        /// 保存当前表单为配置方案
        /// </summary>
        private async Task SaveCurrentProfileAsync()
        {
            NetworkAdapter adapter = SelectedAdapter;
            string name = await Sheets.PromptAsync(this, btnProfile, "保存配置方案", "方案名称", adapter == null ? "" : adapter.Name, Glyph.Layers);
            if (string.IsNullOrWhiteSpace(name)) return;

            bool isDhcp = !IsStatic;
            bool manualDns = IsManualDns;
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
                ShowValidationErrors(errors);
                SetStatus("当前配置有误，无法保存：" + errors[0].Message, StatusKind.Error);
                return;
            }
            if (ProfileManager.Find(name) != null
                && !await ConfirmAsync(btnProfile, "保存配置", $"已存在名为“{name}”的配置方案，是否覆盖？", "覆盖"))
                return;

            try
            {
                ProfileManager.Add(profile);
                SetStatus($"配置方案“{name}”已保存", StatusKind.Success);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                SetStatus("保存失败：" + ex.Message, StatusKind.Error);
            }
        }

        /// <summary>
        /// 载入配置方案到表单，并询问是否立即应用。方案绑定了网卡时先切换到该网卡。
        /// </summary>
        private async Task ApplyProfileAsync(IpProfile profile, System.Windows.Forms.Control origin)
        {
            if (isBusy) return;

            if (!string.IsNullOrEmpty(profile.AdapterMac))
            {
                NetworkAdapter bound = adapters.FirstOrDefault(a => a.MacAddressText == profile.AdapterMac);
                if (bound == null)
                {
                    if (!await ConfirmAsync(origin, "应用配置方案", $"方案“{profile.Name}”绑定的网卡（{profile.AdapterMac}）不在列表中。是否应用到当前选中的网卡？", "应用到当前网卡"))
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
            if (await ConfirmAsync(origin, "应用配置方案", $"已载入配置方案“{profile.Name}”。是否立即应用到“{adapter.Name}”？", "立即应用"))
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

        #region 命令面板

        private List<PaletteCommand> BuildCommands()
        {
            NetworkAdapter adapter = SelectedAdapter;
            var list = new List<PaletteCommand>();
            const string config = "配置";
            if (btnApply.Enabled) list.Add(new PaletteCommand(config, "应用当前配置", Glyph.Check, () => btnApply.PerformClick()) { Hint = "Enter" });
            if (btnRevert.Enabled) list.Add(new PaletteCommand(config, "还原为网卡当前配置", Glyph.Undo, () => btnRevert.PerformClick()) { Hint = "Esc" });
            if (segIpMode.Enabled)
            {
                list.Add(new PaletteCommand(config, "IP 改为自动获取（DHCP）", Glyph.Refresh, () => segIpMode.SelectedIndex = 0) { Keywords = "dhcp 自动" });
                if (segIpMode.IsItemEnabled(1)) list.Add(new PaletteCommand(config, "IP 改为手动设置", Glyph.Monitor, () =>
                {
                    segIpMode.SelectedIndex = 1;
                    txtIpAddress.FocusInput();
                }) { Keywords = "static 静态 手动" });
            }
            if (btnDnsPreset.Enabled)
            {
                foreach (DnsCandidate preset in BuiltInAndCustomPresets())
                {
                    string[] servers = preset.Servers;
                    list.Add(new PaletteCommand("DNS", "使用 " + preset.Name, Glyph.Server, () => ApplyDnsPreset(servers)) { Hint = servers[0], Keywords = "dns 预设" });
                }
            }
            if (btnDnsBenchmark.Enabled) list.Add(new PaletteCommand("DNS", "DNS 测速", Glyph.Gauge, OpenDnsBenchmark) { Keywords = "dns 速度 延迟" });

            foreach (IpProfile p in ProfileManager.Profiles)
            {
                IpProfile profile = p;
                list.Add(new PaletteCommand("配置方案", "载入方案 " + profile.Name, Glyph.Layers, async () => await ApplyProfileAsync(profile, btnProfile)) { Hint = profile.Summary });
            }
            if (btnProfile.Enabled)
            {
                list.Add(new PaletteCommand("配置方案", "保存当前配置为方案", Glyph.Save, async () => await SaveCurrentProfileAsync()));
                list.Add(new PaletteCommand("配置方案", "管理配置方案", Glyph.Pencil, async () => await OpenProfileManagerAsync()));
            }

            const string tools = "工具";
            list.Add(new PaletteCommand(tools, "网络诊断", Glyph.Activity, () => btnDiagnose.PerformClick()) { Keywords = "诊断 检测 上不了网 diagnose" });
            list.Add(new PaletteCommand(tools, "网卡信息报表", Glyph.FileText, () => btnReport.PerformClick()) { Keywords = "报表 report 导出" });
            list.Add(new PaletteCommand(tools, "打开系统“网络连接”", Glyph.ExternalLink, OpenNetworkConnections) { Keywords = "ncpa" });

            const string adapterGroup = "网卡";
            if (btnRefresh.Enabled) list.Add(new PaletteCommand(adapterGroup, "刷新网卡列表", Glyph.Refresh, () => btnRefresh.PerformClick()));
            if (adapter != null && btnToggleAdapter.Enabled)
                list.Add(new PaletteCommand(adapterGroup, btnToggleAdapter.Text + "网卡 " + adapter.Name, Glyph.Power, () => btnToggleAdapter.PerformClick()));
            if (adapter != null && adapter.IsDhcpEnabled && adapter.ConfigBackend == BackendKind.Wmi && !isBusy)
                list.Add(new PaletteCommand(adapterGroup, "重新获取 IP（续订 DHCP 租约）", Glyph.Refresh, async () => await RenewDhcpAsync()) { Keywords = "renew dhcp" });
            foreach (NetworkAdapter a in adapters)
            {
                NetworkAdapter target = a;
                if (target == adapter || !cbxNetworkAdapter.Enabled) continue;
                list.Add(new PaletteCommand(adapterGroup, "切换到 " + target.Name, AdapterGlyph(target), () => cbxNetworkAdapter.SelectedItem = target) { Hint = target.StatusText });
            }
            if (chkShowAll.Enabled) list.Add(new PaletteCommand(adapterGroup, chkShowAll.Checked ? "只显示物理网卡" : "显示全部网卡（含虚拟网卡）", Glyph.Network, () => chkShowAll.Checked = !chkShowAll.Checked));

            const string prefs = "设置";
            list.Add(new PaletteCommand(prefs, (settings.MinimizeToTray ? "关闭" : "开启") + "“关闭时最小化到托盘”", Glyph.Tray, () =>
            {
                settings.MinimizeToTray = !settings.MinimizeToTray;
                SaveSettings();
                SetStatus(settings.MinimizeToTray ? "关闭窗口时将最小化到托盘" : "关闭窗口时将直接退出", StatusKind.Success);
            }));
            list.Add(new PaletteCommand(prefs, (AutoStart.IsEnabled ? "关闭" : "开启") + "开机自动启动", Glyph.Zap, ToggleAutoStart));
            if (showElevation) list.Add(new PaletteCommand(prefs, "以管理员身份重启", Glyph.Shield, RestartAsAdministrator) { Keywords = "admin 管理员 权限" });
            list.Add(new PaletteCommand(prefs, "退出程序", Glyph.LogOut, ExitApplication) { Keywords = "exit quit" });
            return list;
        }

        internal async void OpenCommandPalette()
        {
            if (HasSheet || !Visible) return;
            var palette = new CommandPalette(BuildCommands());
            System.Drawing.Rectangle origin = TitleBar.SearchBounds;
            object result = await SheetOverlay.Show(this, palette, origin, Palette.Surface, P(Metrics.ButtonRadius));
            var command = result as PaletteCommand;
            if (command != null && !IsDisposed) command.Run();
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
            Menus.Clear(trayMenu);
            trayMenu.Items.Add(Menus.Item("显示主界面", Glyph.AppWindow, (s, e) => ShowFromTray()));
            trayMenu.Items.Add(new ToolStripSeparator());

            // 配置方案：一键载入并确认应用
            foreach (IpProfile p in ProfileManager.Profiles)
            {
                IpProfile profile = p;
                trayMenu.Items.Add(Menus.Item(profile.Name, profile.Summary, Glyph.Layers, async (s, e) =>
                {
                    ShowFromTray();
                    await ApplyProfileAsync(profile, null);
                }));
            }
            trayMenu.Items.Add(Menus.Item("管理配置方案…", Glyph.Pencil, async (s, e) =>
            {
                ShowFromTray();
                await OpenProfileManagerAsync();
            }));

            trayMenu.Items.Add(new ToolStripSeparator());
            AddSettingItems(trayMenu.Items);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(Menus.Item("退出", Glyph.LogOut, (s, e) => ExitApplication()));
        }

        /// <summary>
        /// 设置项，托盘菜单和“更多”菜单共用
        /// </summary>
        private void AddSettingItems(ToolStripItemCollection items)
        {
            ToolStripMenuItem tray = Menus.Item("关闭时最小化到托盘", Glyph.Tray, (s, e) =>
            {
                settings.MinimizeToTray = !settings.MinimizeToTray;
                SaveSettings();
            });
            tray.Checked = settings.MinimizeToTray;
            ToolStripMenuItem autoStart = Menus.Item("开机自动启动（只显示托盘图标）", Glyph.Zap, (s, e) => ToggleAutoStart());
            autoStart.Checked = AutoStart.IsEnabled;
            items.Add(tray);
            items.Add(autoStart);
        }

        private void ToggleAutoStart()
        {
            bool enable = !AutoStart.IsEnabled;
            try
            {
                AutoStart.Set(enable, Application.ExecutablePath);
                SetStatus(enable ? "已设置开机自动启动" : "已取消开机自动启动", enable ? StatusKind.Success : StatusKind.Info);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is IOException)
            {
                SetStatus("设置开机自动启动失败：" + ex.Message, StatusKind.Error);
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
            bool wasHidden = !Visible || WindowState == FormWindowState.Minimized;
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            ShowInTaskbar = true;
            Activate();
            BringToFront();
            if (wasHidden) PlayEntrance();
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
                SetStatus("网络配置已变化，点击刷新查看最新配置（会放弃未应用的修改）");
                return;
            }
            await RefreshAdaptersAsync();
        }

        #endregion
    }
}
