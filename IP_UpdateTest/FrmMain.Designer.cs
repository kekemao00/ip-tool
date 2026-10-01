namespace IP_UpdateTest
{
    partial class FrmMain
    {
        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.pnlTitle = new System.Windows.Forms.Panel();
            this.pnlElevation = new System.Windows.Forms.Panel();
            this.lblElevation = new System.Windows.Forms.Label();
            this.btnRestartAsAdmin = new System.Windows.Forms.Button();
            this.pnlAdapter = new System.Windows.Forms.Panel();
            this.tlpAdapter = new System.Windows.Forms.TableLayoutPanel();
            this.lblAdapter = new System.Windows.Forms.Label();
            this.cbxNetworkAdapter = new System.Windows.Forms.ComboBox();
            this.btnRefresh = new System.Windows.Forms.Button();
            this.tlpAdapterInfo = new System.Windows.Forms.TableLayoutPanel();
            this.lblAdapterStatus = new System.Windows.Forms.Label();
            this.chkShowAll = new System.Windows.Forms.CheckBox();
            this.btnToggleAdapter = new System.Windows.Forms.Button();
            this.btnMore = new System.Windows.Forms.Button();
            this.tlpForm = new System.Windows.Forms.TableLayoutPanel();
            this.lblIpMode = new System.Windows.Forms.Label();
            this.flpIpMode = new System.Windows.Forms.FlowLayoutPanel();
            this.rbIpDhcp = new System.Windows.Forms.RadioButton();
            this.rbIpStatic = new System.Windows.Forms.RadioButton();
            this.lblIpAddress = new System.Windows.Forms.Label();
            this.txtIpAddress = new System.Windows.Forms.TextBox();
            this.lblMask = new System.Windows.Forms.Label();
            this.txtMask = new System.Windows.Forms.TextBox();
            this.lblGateway = new System.Windows.Forms.Label();
            this.txtGateway = new System.Windows.Forms.TextBox();
            this.lblDnsMode = new System.Windows.Forms.Label();
            this.flpDnsMode = new System.Windows.Forms.FlowLayoutPanel();
            this.rbDnsAuto = new System.Windows.Forms.RadioButton();
            this.rbDnsManual = new System.Windows.Forms.RadioButton();
            this.btnDnsPreset = new System.Windows.Forms.Button();
            this.btnDnsBenchmark = new System.Windows.Forms.Button();
            this.lblDnsMain = new System.Windows.Forms.Label();
            this.txtDnsMain = new System.Windows.Forms.TextBox();
            this.lblDnsBackup = new System.Windows.Forms.Label();
            this.txtDnsBackup = new System.Windows.Forms.TextBox();
            this.pnlSeparator = new System.Windows.Forms.Panel();
            this.lblMacCaption = new System.Windows.Forms.Label();
            this.txtMac = new System.Windows.Forms.TextBox();
            this.lblDhcpCaption = new System.Windows.Forms.Label();
            this.txtDhcpServer = new System.Windows.Forms.TextBox();
            this.lblIPv6Caption = new System.Windows.Forms.Label();
            this.txtIPv6 = new System.Windows.Forms.TextBox();
            this.pnlActions = new System.Windows.Forms.Panel();
            this.tlpActions = new System.Windows.Forms.TableLayoutPanel();
            this.btnProfile = new System.Windows.Forms.Button();
            this.btnDiagnose = new System.Windows.Forms.Button();
            this.btnReport = new System.Windows.Forms.Button();
            this.btnRevert = new System.Windows.Forms.Button();
            this.btnApply = new System.Windows.Forms.Button();
            this.pnlStatus = new System.Windows.Forms.Panel();
            this.lblStatus = new System.Windows.Forms.Label();
            this.errorProvider = new System.Windows.Forms.ErrorProvider(this.components);
            this.toolTip = new System.Windows.Forms.ToolTip(this.components);
            this.pnlElevation.SuspendLayout();
            this.pnlAdapter.SuspendLayout();
            this.tlpAdapter.SuspendLayout();
            this.tlpAdapterInfo.SuspendLayout();
            this.tlpForm.SuspendLayout();
            this.flpIpMode.SuspendLayout();
            this.flpDnsMode.SuspendLayout();
            this.pnlActions.SuspendLayout();
            this.tlpActions.SuspendLayout();
            this.pnlStatus.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.errorProvider)).BeginInit();
            this.SuspendLayout();
            //
            // pnlTitle
            //
            this.pnlTitle.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlTitle.Location = new System.Drawing.Point(0, 0);
            this.pnlTitle.Name = "pnlTitle";
            this.pnlTitle.Size = new System.Drawing.Size(460, 40);
            this.pnlTitle.TabIndex = 5;
            //
            // pnlElevation
            //
            this.pnlElevation.Controls.Add(this.lblElevation);
            this.pnlElevation.Controls.Add(this.btnRestartAsAdmin);
            this.pnlElevation.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlElevation.Location = new System.Drawing.Point(0, 40);
            this.pnlElevation.Name = "pnlElevation";
            this.pnlElevation.Padding = new System.Windows.Forms.Padding(16, 0, 8, 0);
            this.pnlElevation.Size = new System.Drawing.Size(460, 34);
            this.pnlElevation.TabIndex = 3;
            //
            // lblElevation
            //
            this.lblElevation.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblElevation.Location = new System.Drawing.Point(16, 0);
            this.lblElevation.Name = "lblElevation";
            this.lblElevation.Size = new System.Drawing.Size(320, 34);
            this.lblElevation.TabIndex = 1;
            this.lblElevation.Text = "当前为普通权限：可以查看，修改配置需要管理员权限";
            this.lblElevation.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // btnRestartAsAdmin
            //
            this.btnRestartAsAdmin.AutoSize = true;
            this.btnRestartAsAdmin.Dock = System.Windows.Forms.DockStyle.Right;
            this.btnRestartAsAdmin.Location = new System.Drawing.Point(336, 0);
            this.btnRestartAsAdmin.Name = "btnRestartAsAdmin";
            this.btnRestartAsAdmin.Size = new System.Drawing.Size(116, 34);
            this.btnRestartAsAdmin.TabIndex = 0;
            this.btnRestartAsAdmin.Text = "以管理员身份重启";
            this.btnRestartAsAdmin.UseVisualStyleBackColor = true;
            this.btnRestartAsAdmin.Click += new System.EventHandler(this.btnRestartAsAdmin_Click);
            //
            // pnlAdapter
            //
            this.pnlAdapter.Controls.Add(this.tlpAdapter);
            this.pnlAdapter.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlAdapter.Location = new System.Drawing.Point(0, 74);
            this.pnlAdapter.Name = "pnlAdapter";
            this.pnlAdapter.Padding = new System.Windows.Forms.Padding(16, 10, 16, 2);
            this.pnlAdapter.Size = new System.Drawing.Size(460, 74);
            this.pnlAdapter.TabIndex = 0;
            //
            // tlpAdapter
            //
            this.tlpAdapter.ColumnCount = 3;
            this.tlpAdapter.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 84F));
            this.tlpAdapter.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tlpAdapter.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 36F));
            this.tlpAdapter.Controls.Add(this.lblAdapter, 0, 0);
            this.tlpAdapter.Controls.Add(this.cbxNetworkAdapter, 1, 0);
            this.tlpAdapter.Controls.Add(this.btnRefresh, 2, 0);
            this.tlpAdapter.Controls.Add(this.tlpAdapterInfo, 1, 1);
            this.tlpAdapter.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tlpAdapter.Location = new System.Drawing.Point(16, 10);
            this.tlpAdapter.Margin = new System.Windows.Forms.Padding(0);
            this.tlpAdapter.Name = "tlpAdapter";
            this.tlpAdapter.RowCount = 2;
            this.tlpAdapter.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 32F));
            this.tlpAdapter.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 30F));
            this.tlpAdapter.Size = new System.Drawing.Size(428, 62);
            this.tlpAdapter.TabIndex = 0;
            //
            // lblAdapter
            //
            this.lblAdapter.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lblAdapter.AutoSize = true;
            this.lblAdapter.Location = new System.Drawing.Point(0, 8);
            this.lblAdapter.Margin = new System.Windows.Forms.Padding(0);
            this.lblAdapter.Name = "lblAdapter";
            this.lblAdapter.Size = new System.Drawing.Size(32, 17);
            this.lblAdapter.TabIndex = 9;
            this.lblAdapter.Text = "网卡";
            //
            // cbxNetworkAdapter
            //
            this.cbxNetworkAdapter.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.cbxNetworkAdapter.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cbxNetworkAdapter.FormattingEnabled = true;
            this.cbxNetworkAdapter.Location = new System.Drawing.Point(84, 4);
            this.cbxNetworkAdapter.Margin = new System.Windows.Forms.Padding(0);
            this.cbxNetworkAdapter.Name = "cbxNetworkAdapter";
            this.cbxNetworkAdapter.Size = new System.Drawing.Size(308, 25);
            this.cbxNetworkAdapter.TabIndex = 0;
            this.cbxNetworkAdapter.SelectedIndexChanged += new System.EventHandler(this.cbxNetworkAdapter_SelectedIndexChanged);
            //
            // btnRefresh
            //
            this.btnRefresh.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.btnRefresh.Location = new System.Drawing.Point(398, 2);
            this.btnRefresh.Margin = new System.Windows.Forms.Padding(0);
            this.btnRefresh.Name = "btnRefresh";
            this.btnRefresh.Size = new System.Drawing.Size(30, 28);
            this.btnRefresh.TabIndex = 1;
            this.btnRefresh.Text = "⟳";
            this.btnRefresh.UseVisualStyleBackColor = true;
            this.btnRefresh.Click += new System.EventHandler(this.btnRefresh_Click);
            //
            // tlpAdapterInfo
            //
            this.tlpAdapterInfo.ColumnCount = 4;
            this.tlpAdapter.SetColumnSpan(this.tlpAdapterInfo, 2);
            this.tlpAdapterInfo.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tlpAdapterInfo.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.tlpAdapterInfo.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.tlpAdapterInfo.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.tlpAdapterInfo.Controls.Add(this.lblAdapterStatus, 0, 0);
            this.tlpAdapterInfo.Controls.Add(this.chkShowAll, 1, 0);
            this.tlpAdapterInfo.Controls.Add(this.btnToggleAdapter, 2, 0);
            this.tlpAdapterInfo.Controls.Add(this.btnMore, 3, 0);
            this.tlpAdapterInfo.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tlpAdapterInfo.Location = new System.Drawing.Point(84, 32);
            this.tlpAdapterInfo.Margin = new System.Windows.Forms.Padding(0);
            this.tlpAdapterInfo.Name = "tlpAdapterInfo";
            this.tlpAdapterInfo.RowCount = 1;
            this.tlpAdapterInfo.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tlpAdapterInfo.Size = new System.Drawing.Size(344, 30);
            this.tlpAdapterInfo.TabIndex = 2;
            //
            // lblAdapterStatus
            //
            this.lblAdapterStatus.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.lblAdapterStatus.AutoEllipsis = true;
            this.lblAdapterStatus.Location = new System.Drawing.Point(0, 6);
            this.lblAdapterStatus.Margin = new System.Windows.Forms.Padding(0);
            this.lblAdapterStatus.Name = "lblAdapterStatus";
            this.lblAdapterStatus.Size = new System.Drawing.Size(132, 17);
            this.lblAdapterStatus.TabIndex = 3;
            //
            // chkShowAll
            //
            this.chkShowAll.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.chkShowAll.AutoSize = true;
            this.chkShowAll.Location = new System.Drawing.Point(132, 5);
            this.chkShowAll.Margin = new System.Windows.Forms.Padding(0, 0, 4, 0);
            this.chkShowAll.Name = "chkShowAll";
            this.chkShowAll.Size = new System.Drawing.Size(75, 21);
            this.chkShowAll.TabIndex = 0;
            this.chkShowAll.Text = "显示全部";
            this.chkShowAll.UseVisualStyleBackColor = true;
            this.chkShowAll.CheckedChanged += new System.EventHandler(this.chkShowAll_CheckedChanged);
            //
            // btnToggleAdapter
            //
            this.btnToggleAdapter.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.btnToggleAdapter.Location = new System.Drawing.Point(211, 2);
            this.btnToggleAdapter.Margin = new System.Windows.Forms.Padding(0, 0, 4, 0);
            this.btnToggleAdapter.Name = "btnToggleAdapter";
            this.btnToggleAdapter.Size = new System.Drawing.Size(60, 26);
            this.btnToggleAdapter.TabIndex = 1;
            this.btnToggleAdapter.Text = "禁用";
            this.btnToggleAdapter.UseVisualStyleBackColor = true;
            this.btnToggleAdapter.Click += new System.EventHandler(this.btnToggleAdapter_Click);
            //
            // btnMore
            //
            this.btnMore.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.btnMore.Location = new System.Drawing.Point(275, 2);
            this.btnMore.Margin = new System.Windows.Forms.Padding(0);
            this.btnMore.Name = "btnMore";
            this.btnMore.Size = new System.Drawing.Size(69, 26);
            this.btnMore.TabIndex = 2;
            this.btnMore.Text = "更多 ▾";
            this.btnMore.UseVisualStyleBackColor = true;
            this.btnMore.Click += new System.EventHandler(this.btnMore_Click);
            //
            // tlpForm
            //
            this.tlpForm.ColumnCount = 2;
            this.tlpForm.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 84F));
            this.tlpForm.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tlpForm.Controls.Add(this.lblIpMode, 0, 0);
            this.tlpForm.Controls.Add(this.flpIpMode, 1, 0);
            this.tlpForm.Controls.Add(this.lblIpAddress, 0, 1);
            this.tlpForm.Controls.Add(this.txtIpAddress, 1, 1);
            this.tlpForm.Controls.Add(this.lblMask, 0, 2);
            this.tlpForm.Controls.Add(this.txtMask, 1, 2);
            this.tlpForm.Controls.Add(this.lblGateway, 0, 3);
            this.tlpForm.Controls.Add(this.txtGateway, 1, 3);
            this.tlpForm.Controls.Add(this.lblDnsMode, 0, 4);
            this.tlpForm.Controls.Add(this.flpDnsMode, 1, 4);
            this.tlpForm.Controls.Add(this.lblDnsMain, 0, 5);
            this.tlpForm.Controls.Add(this.txtDnsMain, 1, 5);
            this.tlpForm.Controls.Add(this.lblDnsBackup, 0, 6);
            this.tlpForm.Controls.Add(this.txtDnsBackup, 1, 6);
            this.tlpForm.Controls.Add(this.pnlSeparator, 0, 7);
            this.tlpForm.Controls.Add(this.lblMacCaption, 0, 8);
            this.tlpForm.Controls.Add(this.txtMac, 1, 8);
            this.tlpForm.Controls.Add(this.lblDhcpCaption, 0, 9);
            this.tlpForm.Controls.Add(this.txtDhcpServer, 1, 9);
            this.tlpForm.Controls.Add(this.lblIPv6Caption, 0, 10);
            this.tlpForm.Controls.Add(this.txtIPv6, 1, 10);
            this.tlpForm.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tlpForm.Location = new System.Drawing.Point(0, 148);
            this.tlpForm.Name = "tlpForm";
            this.tlpForm.Padding = new System.Windows.Forms.Padding(16, 6, 16, 6);
            this.tlpForm.RowCount = 11;
            this.tlpForm.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 34F));
            this.tlpForm.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 34F));
            this.tlpForm.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 34F));
            this.tlpForm.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 34F));
            this.tlpForm.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 34F));
            this.tlpForm.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 34F));
            this.tlpForm.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 34F));
            this.tlpForm.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 17F));
            this.tlpForm.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 26F));
            this.tlpForm.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 26F));
            this.tlpForm.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tlpForm.Size = new System.Drawing.Size(460, 364);
            this.tlpForm.TabIndex = 1;
            //
            // lblIpMode
            //
            this.lblIpMode.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lblIpMode.AutoSize = true;
            this.lblIpMode.Location = new System.Drawing.Point(16, 14);
            this.lblIpMode.Margin = new System.Windows.Forms.Padding(0);
            this.lblIpMode.Name = "lblIpMode";
            this.lblIpMode.Size = new System.Drawing.Size(56, 17);
            this.lblIpMode.TabIndex = 20;
            this.lblIpMode.Text = "IP 获取";
            //
            // flpIpMode
            //
            this.flpIpMode.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.flpIpMode.AutoSize = true;
            this.flpIpMode.Controls.Add(this.rbIpDhcp);
            this.flpIpMode.Controls.Add(this.rbIpStatic);
            this.flpIpMode.Location = new System.Drawing.Point(100, 11);
            this.flpIpMode.Margin = new System.Windows.Forms.Padding(0);
            this.flpIpMode.Name = "flpIpMode";
            this.flpIpMode.Size = new System.Drawing.Size(344, 24);
            this.flpIpMode.TabIndex = 0;
            this.flpIpMode.WrapContents = false;
            //
            // rbIpDhcp
            //
            this.rbIpDhcp.AutoSize = true;
            this.rbIpDhcp.Location = new System.Drawing.Point(0, 2);
            this.rbIpDhcp.Margin = new System.Windows.Forms.Padding(0, 2, 20, 2);
            this.rbIpDhcp.Name = "rbIpDhcp";
            this.rbIpDhcp.Size = new System.Drawing.Size(117, 21);
            this.rbIpDhcp.TabIndex = 0;
            this.rbIpDhcp.TabStop = true;
            this.rbIpDhcp.Text = "自动获取 (DHCP)";
            this.rbIpDhcp.UseVisualStyleBackColor = true;
            this.rbIpDhcp.CheckedChanged += new System.EventHandler(this.IpMode_CheckedChanged);
            //
            // rbIpStatic
            //
            this.rbIpStatic.AutoSize = true;
            this.rbIpStatic.Location = new System.Drawing.Point(137, 2);
            this.rbIpStatic.Margin = new System.Windows.Forms.Padding(0, 2, 0, 2);
            this.rbIpStatic.Name = "rbIpStatic";
            this.rbIpStatic.Size = new System.Drawing.Size(74, 21);
            this.rbIpStatic.TabIndex = 1;
            this.rbIpStatic.Text = "手动设置";
            this.rbIpStatic.UseVisualStyleBackColor = true;
            this.rbIpStatic.CheckedChanged += new System.EventHandler(this.IpMode_CheckedChanged);
            //
            // lblIpAddress
            //
            this.lblIpAddress.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lblIpAddress.AutoSize = true;
            this.lblIpAddress.Location = new System.Drawing.Point(16, 48);
            this.lblIpAddress.Margin = new System.Windows.Forms.Padding(0);
            this.lblIpAddress.Name = "lblIpAddress";
            this.lblIpAddress.Size = new System.Drawing.Size(51, 17);
            this.lblIpAddress.TabIndex = 21;
            this.lblIpAddress.Text = "IP 地址";
            //
            // txtIpAddress
            //
            this.txtIpAddress.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.txtIpAddress.Location = new System.Drawing.Point(100, 45);
            this.txtIpAddress.Margin = new System.Windows.Forms.Padding(0, 0, 20, 0);
            this.txtIpAddress.Name = "txtIpAddress";
            this.txtIpAddress.Size = new System.Drawing.Size(324, 23);
            this.txtIpAddress.TabIndex = 1;
            this.txtIpAddress.TextChanged += new System.EventHandler(this.Field_TextChanged);
            this.txtIpAddress.Leave += new System.EventHandler(this.txtIpAddress_Leave);
            //
            // lblMask
            //
            this.lblMask.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lblMask.AutoSize = true;
            this.lblMask.Location = new System.Drawing.Point(16, 82);
            this.lblMask.Margin = new System.Windows.Forms.Padding(0);
            this.lblMask.Name = "lblMask";
            this.lblMask.Size = new System.Drawing.Size(56, 17);
            this.lblMask.TabIndex = 22;
            this.lblMask.Text = "子网掩码";
            //
            // txtMask
            //
            this.txtMask.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.txtMask.Location = new System.Drawing.Point(100, 79);
            this.txtMask.Margin = new System.Windows.Forms.Padding(0, 0, 20, 0);
            this.txtMask.Name = "txtMask";
            this.txtMask.Size = new System.Drawing.Size(324, 23);
            this.txtMask.TabIndex = 2;
            this.txtMask.TextChanged += new System.EventHandler(this.Field_TextChanged);
            //
            // lblGateway
            //
            this.lblGateway.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lblGateway.AutoSize = true;
            this.lblGateway.Location = new System.Drawing.Point(16, 116);
            this.lblGateway.Margin = new System.Windows.Forms.Padding(0);
            this.lblGateway.Name = "lblGateway";
            this.lblGateway.Size = new System.Drawing.Size(56, 17);
            this.lblGateway.TabIndex = 23;
            this.lblGateway.Text = "默认网关";
            //
            // txtGateway
            //
            this.txtGateway.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.txtGateway.Location = new System.Drawing.Point(100, 113);
            this.txtGateway.Margin = new System.Windows.Forms.Padding(0, 0, 20, 0);
            this.txtGateway.Name = "txtGateway";
            this.txtGateway.Size = new System.Drawing.Size(324, 23);
            this.txtGateway.TabIndex = 3;
            this.txtGateway.TextChanged += new System.EventHandler(this.Field_TextChanged);
            //
            // lblDnsMode
            //
            this.lblDnsMode.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lblDnsMode.AutoSize = true;
            this.lblDnsMode.Location = new System.Drawing.Point(16, 150);
            this.lblDnsMode.Margin = new System.Windows.Forms.Padding(0);
            this.lblDnsMode.Name = "lblDnsMode";
            this.lblDnsMode.Size = new System.Drawing.Size(61, 17);
            this.lblDnsMode.TabIndex = 24;
            this.lblDnsMode.Text = "DNS 获取";
            //
            // flpDnsMode
            //
            this.flpDnsMode.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.flpDnsMode.AutoSize = true;
            this.flpDnsMode.Controls.Add(this.rbDnsAuto);
            this.flpDnsMode.Controls.Add(this.rbDnsManual);
            this.flpDnsMode.Controls.Add(this.btnDnsPreset);
            this.flpDnsMode.Controls.Add(this.btnDnsBenchmark);
            this.flpDnsMode.Location = new System.Drawing.Point(100, 144);
            this.flpDnsMode.Margin = new System.Windows.Forms.Padding(0);
            this.flpDnsMode.Name = "flpDnsMode";
            this.flpDnsMode.Size = new System.Drawing.Size(344, 28);
            this.flpDnsMode.TabIndex = 4;
            this.flpDnsMode.WrapContents = false;
            //
            // rbDnsAuto
            //
            this.rbDnsAuto.AutoSize = true;
            this.rbDnsAuto.Location = new System.Drawing.Point(0, 4);
            this.rbDnsAuto.Margin = new System.Windows.Forms.Padding(0, 4, 20, 2);
            this.rbDnsAuto.Name = "rbDnsAuto";
            this.rbDnsAuto.Size = new System.Drawing.Size(74, 21);
            this.rbDnsAuto.TabIndex = 0;
            this.rbDnsAuto.TabStop = true;
            this.rbDnsAuto.Text = "自动获取";
            this.rbDnsAuto.UseVisualStyleBackColor = true;
            this.rbDnsAuto.CheckedChanged += new System.EventHandler(this.DnsMode_CheckedChanged);
            //
            // rbDnsManual
            //
            this.rbDnsManual.AutoSize = true;
            this.rbDnsManual.Location = new System.Drawing.Point(94, 4);
            this.rbDnsManual.Margin = new System.Windows.Forms.Padding(0, 4, 12, 2);
            this.rbDnsManual.Name = "rbDnsManual";
            this.rbDnsManual.Size = new System.Drawing.Size(74, 21);
            this.rbDnsManual.TabIndex = 1;
            this.rbDnsManual.Text = "手动设置";
            this.rbDnsManual.UseVisualStyleBackColor = true;
            this.rbDnsManual.CheckedChanged += new System.EventHandler(this.DnsMode_CheckedChanged);
            //
            // btnDnsPreset
            //
            this.btnDnsPreset.Location = new System.Drawing.Point(180, 1);
            this.btnDnsPreset.Margin = new System.Windows.Forms.Padding(0);
            this.btnDnsPreset.Name = "btnDnsPreset";
            this.btnDnsPreset.Size = new System.Drawing.Size(72, 26);
            this.btnDnsPreset.TabIndex = 2;
            this.btnDnsPreset.Text = "预设 ▾";
            this.btnDnsPreset.UseVisualStyleBackColor = true;
            this.btnDnsPreset.Click += new System.EventHandler(this.btnDnsPreset_Click);
            //
            // btnDnsBenchmark
            //
            this.btnDnsBenchmark.Location = new System.Drawing.Point(258, 1);
            this.btnDnsBenchmark.Margin = new System.Windows.Forms.Padding(6, 0, 0, 0);
            this.btnDnsBenchmark.Name = "btnDnsBenchmark";
            this.btnDnsBenchmark.Size = new System.Drawing.Size(56, 26);
            this.btnDnsBenchmark.TabIndex = 3;
            this.btnDnsBenchmark.Text = "测速";
            this.btnDnsBenchmark.UseVisualStyleBackColor = true;
            this.btnDnsBenchmark.Click += new System.EventHandler(this.btnDnsBenchmark_Click);
            //
            // lblDnsMain
            //
            this.lblDnsMain.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lblDnsMain.AutoSize = true;
            this.lblDnsMain.Location = new System.Drawing.Point(16, 184);
            this.lblDnsMain.Margin = new System.Windows.Forms.Padding(0);
            this.lblDnsMain.Name = "lblDnsMain";
            this.lblDnsMain.Size = new System.Drawing.Size(61, 17);
            this.lblDnsMain.TabIndex = 25;
            this.lblDnsMain.Text = "首选 DNS";
            //
            // txtDnsMain
            //
            this.txtDnsMain.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.txtDnsMain.Location = new System.Drawing.Point(100, 181);
            this.txtDnsMain.Margin = new System.Windows.Forms.Padding(0, 0, 20, 0);
            this.txtDnsMain.Name = "txtDnsMain";
            this.txtDnsMain.Size = new System.Drawing.Size(324, 23);
            this.txtDnsMain.TabIndex = 5;
            this.txtDnsMain.TextChanged += new System.EventHandler(this.Field_TextChanged);
            //
            // lblDnsBackup
            //
            this.lblDnsBackup.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lblDnsBackup.AutoSize = true;
            this.lblDnsBackup.Location = new System.Drawing.Point(16, 218);
            this.lblDnsBackup.Margin = new System.Windows.Forms.Padding(0);
            this.lblDnsBackup.Name = "lblDnsBackup";
            this.lblDnsBackup.Size = new System.Drawing.Size(61, 17);
            this.lblDnsBackup.TabIndex = 26;
            this.lblDnsBackup.Text = "备用 DNS";
            //
            // txtDnsBackup
            //
            this.txtDnsBackup.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.txtDnsBackup.Location = new System.Drawing.Point(100, 215);
            this.txtDnsBackup.Margin = new System.Windows.Forms.Padding(0, 0, 20, 0);
            this.txtDnsBackup.Name = "txtDnsBackup";
            this.txtDnsBackup.Size = new System.Drawing.Size(324, 23);
            this.txtDnsBackup.TabIndex = 6;
            this.txtDnsBackup.TextChanged += new System.EventHandler(this.Field_TextChanged);
            //
            // pnlSeparator
            //
            this.pnlSeparator.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.tlpForm.SetColumnSpan(this.pnlSeparator, 2);
            this.pnlSeparator.Location = new System.Drawing.Point(16, 252);
            this.pnlSeparator.Margin = new System.Windows.Forms.Padding(0);
            this.pnlSeparator.Name = "pnlSeparator";
            this.pnlSeparator.Size = new System.Drawing.Size(428, 1);
            this.pnlSeparator.TabIndex = 27;
            //
            // lblMacCaption
            //
            this.lblMacCaption.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lblMacCaption.AutoSize = true;
            this.lblMacCaption.Location = new System.Drawing.Point(16, 266);
            this.lblMacCaption.Margin = new System.Windows.Forms.Padding(0);
            this.lblMacCaption.Name = "lblMacCaption";
            this.lblMacCaption.Size = new System.Drawing.Size(61, 17);
            this.lblMacCaption.TabIndex = 28;
            this.lblMacCaption.Text = "MAC 地址";
            //
            // txtMac
            //
            this.txtMac.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.txtMac.Location = new System.Drawing.Point(100, 266);
            this.txtMac.Margin = new System.Windows.Forms.Padding(0);
            this.txtMac.Name = "txtMac";
            this.txtMac.Size = new System.Drawing.Size(344, 16);
            this.txtMac.TabIndex = 7;
            this.txtMac.TabStop = false;
            //
            // lblDhcpCaption
            //
            this.lblDhcpCaption.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lblDhcpCaption.AutoSize = true;
            this.lblDhcpCaption.Location = new System.Drawing.Point(16, 292);
            this.lblDhcpCaption.Margin = new System.Windows.Forms.Padding(0);
            this.lblDhcpCaption.Name = "lblDhcpCaption";
            this.lblDhcpCaption.Size = new System.Drawing.Size(77, 17);
            this.lblDhcpCaption.TabIndex = 29;
            this.lblDhcpCaption.Text = "DHCP 服务器";
            //
            // txtDhcpServer
            //
            this.txtDhcpServer.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.txtDhcpServer.Location = new System.Drawing.Point(100, 292);
            this.txtDhcpServer.Margin = new System.Windows.Forms.Padding(0);
            this.txtDhcpServer.Name = "txtDhcpServer";
            this.txtDhcpServer.Size = new System.Drawing.Size(344, 16);
            this.txtDhcpServer.TabIndex = 8;
            this.txtDhcpServer.TabStop = false;
            //
            // lblIPv6Caption
            //
            this.lblIPv6Caption.AutoSize = true;
            this.lblIPv6Caption.Location = new System.Drawing.Point(16, 319);
            this.lblIPv6Caption.Margin = new System.Windows.Forms.Padding(0, 4, 0, 0);
            this.lblIPv6Caption.Name = "lblIPv6Caption";
            this.lblIPv6Caption.Size = new System.Drawing.Size(32, 17);
            this.lblIPv6Caption.TabIndex = 30;
            this.lblIPv6Caption.Text = "IPv6";
            //
            // txtIPv6
            //
            this.txtIPv6.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtIPv6.Location = new System.Drawing.Point(100, 319);
            this.txtIPv6.Margin = new System.Windows.Forms.Padding(0, 4, 0, 0);
            this.txtIPv6.Multiline = true;
            this.txtIPv6.Name = "txtIPv6";
            this.txtIPv6.Size = new System.Drawing.Size(344, 39);
            this.txtIPv6.TabIndex = 9;
            this.txtIPv6.TabStop = false;
            //
            // pnlActions
            //
            this.pnlActions.Controls.Add(this.tlpActions);
            this.pnlActions.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.pnlActions.Location = new System.Drawing.Point(0, 512);
            this.pnlActions.Name = "pnlActions";
            this.pnlActions.Padding = new System.Windows.Forms.Padding(16, 10, 16, 10);
            this.pnlActions.Size = new System.Drawing.Size(460, 52);
            this.pnlActions.TabIndex = 2;
            //
            // tlpActions
            //
            this.tlpActions.ColumnCount = 6;
            this.tlpActions.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.tlpActions.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.tlpActions.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.tlpActions.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tlpActions.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.tlpActions.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.tlpActions.Controls.Add(this.btnProfile, 0, 0);
            this.tlpActions.Controls.Add(this.btnDiagnose, 1, 0);
            this.tlpActions.Controls.Add(this.btnReport, 2, 0);
            this.tlpActions.Controls.Add(this.btnRevert, 4, 0);
            this.tlpActions.Controls.Add(this.btnApply, 5, 0);
            this.tlpActions.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tlpActions.Location = new System.Drawing.Point(16, 10);
            this.tlpActions.Margin = new System.Windows.Forms.Padding(0);
            this.tlpActions.Name = "tlpActions";
            this.tlpActions.RowCount = 1;
            this.tlpActions.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tlpActions.Size = new System.Drawing.Size(428, 32);
            this.tlpActions.TabIndex = 0;
            //
            // btnProfile
            //
            this.btnProfile.Location = new System.Drawing.Point(0, 0);
            this.btnProfile.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
            this.btnProfile.Name = "btnProfile";
            this.btnProfile.Size = new System.Drawing.Size(92, 32);
            this.btnProfile.TabIndex = 0;
            this.btnProfile.Text = "配置方案 ▾";
            this.btnProfile.UseVisualStyleBackColor = true;
            this.btnProfile.Click += new System.EventHandler(this.btnProfile_Click);
            //
            // btnDiagnose
            //
            this.btnDiagnose.Location = new System.Drawing.Point(98, 0);
            this.btnDiagnose.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
            this.btnDiagnose.Name = "btnDiagnose";
            this.btnDiagnose.Size = new System.Drawing.Size(56, 32);
            this.btnDiagnose.TabIndex = 1;
            this.btnDiagnose.Text = "诊断";
            this.btnDiagnose.UseVisualStyleBackColor = true;
            this.btnDiagnose.Click += new System.EventHandler(this.btnDiagnose_Click);
            //
            // btnReport
            //
            this.btnReport.Location = new System.Drawing.Point(160, 0);
            this.btnReport.Margin = new System.Windows.Forms.Padding(0);
            this.btnReport.Name = "btnReport";
            this.btnReport.Size = new System.Drawing.Size(56, 32);
            this.btnReport.TabIndex = 2;
            this.btnReport.Text = "报表";
            this.btnReport.UseVisualStyleBackColor = true;
            this.btnReport.Click += new System.EventHandler(this.btnReport_Click);
            //
            // btnRevert
            //
            this.btnRevert.Location = new System.Drawing.Point(282, 0);
            this.btnRevert.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
            this.btnRevert.Name = "btnRevert";
            this.btnRevert.Size = new System.Drawing.Size(56, 32);
            this.btnRevert.TabIndex = 3;
            this.btnRevert.Text = "还原";
            this.btnRevert.UseVisualStyleBackColor = true;
            this.btnRevert.Click += new System.EventHandler(this.btnRevert_Click);
            //
            // btnApply
            //
            this.btnApply.Location = new System.Drawing.Point(344, 0);
            this.btnApply.Margin = new System.Windows.Forms.Padding(0);
            this.btnApply.Name = "btnApply";
            this.btnApply.Size = new System.Drawing.Size(84, 32);
            this.btnApply.TabIndex = 4;
            this.btnApply.Text = "应用";
            this.btnApply.UseVisualStyleBackColor = true;
            this.btnApply.Click += new System.EventHandler(this.btnApply_Click);
            //
            // pnlStatus
            //
            this.pnlStatus.Controls.Add(this.lblStatus);
            this.pnlStatus.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.pnlStatus.Location = new System.Drawing.Point(0, 564);
            this.pnlStatus.Name = "pnlStatus";
            this.pnlStatus.Padding = new System.Windows.Forms.Padding(16, 0, 16, 0);
            this.pnlStatus.Size = new System.Drawing.Size(460, 28);
            this.pnlStatus.TabIndex = 4;
            //
            // lblStatus
            //
            this.lblStatus.AutoEllipsis = true;
            this.lblStatus.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblStatus.Location = new System.Drawing.Point(16, 0);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(428, 28);
            this.lblStatus.TabIndex = 0;
            this.lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // errorProvider
            //
            this.errorProvider.BlinkStyle = System.Windows.Forms.ErrorBlinkStyle.NeverBlink;
            this.errorProvider.ContainerControl = this;
            //
            // FrmMain
            //
            this.AcceptButton = this.btnApply;
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.CancelButton = this.btnRevert;
            this.ClientSize = new System.Drawing.Size(460, 592);
            this.Controls.Add(this.tlpForm);
            this.Controls.Add(this.pnlActions);
            this.Controls.Add(this.pnlStatus);
            this.Controls.Add(this.pnlAdapter);
            this.Controls.Add(this.pnlElevation);
            this.Controls.Add(this.pnlTitle);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.MaximizeBox = false;
            this.Name = "FrmMain";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "IP Tool";
            this.FormClosed += new System.Windows.Forms.FormClosedEventHandler(this.FrmMain_FormClosed);
            this.Load += new System.EventHandler(this.FrmMain_Load);
            this.pnlElevation.ResumeLayout(false);
            this.pnlElevation.PerformLayout();
            this.pnlAdapter.ResumeLayout(false);
            this.tlpAdapter.ResumeLayout(false);
            this.tlpAdapter.PerformLayout();
            this.tlpAdapterInfo.ResumeLayout(false);
            this.tlpAdapterInfo.PerformLayout();
            this.tlpForm.ResumeLayout(false);
            this.tlpForm.PerformLayout();
            this.flpIpMode.ResumeLayout(false);
            this.flpIpMode.PerformLayout();
            this.flpDnsMode.ResumeLayout(false);
            this.flpDnsMode.PerformLayout();
            this.pnlActions.ResumeLayout(false);
            this.tlpActions.ResumeLayout(false);
            this.pnlStatus.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.errorProvider)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel pnlTitle;
        private System.Windows.Forms.Panel pnlElevation;
        private System.Windows.Forms.Label lblElevation;
        private System.Windows.Forms.Button btnRestartAsAdmin;
        private System.Windows.Forms.Panel pnlAdapter;
        private System.Windows.Forms.TableLayoutPanel tlpAdapter;
        private System.Windows.Forms.Label lblAdapter;
        private System.Windows.Forms.ComboBox cbxNetworkAdapter;
        private System.Windows.Forms.Button btnRefresh;
        private System.Windows.Forms.TableLayoutPanel tlpAdapterInfo;
        private System.Windows.Forms.Label lblAdapterStatus;
        private System.Windows.Forms.CheckBox chkShowAll;
        private System.Windows.Forms.Button btnToggleAdapter;
        private System.Windows.Forms.Button btnMore;
        private System.Windows.Forms.TableLayoutPanel tlpForm;
        private System.Windows.Forms.Label lblIpMode;
        private System.Windows.Forms.FlowLayoutPanel flpIpMode;
        private System.Windows.Forms.RadioButton rbIpDhcp;
        private System.Windows.Forms.RadioButton rbIpStatic;
        private System.Windows.Forms.Label lblIpAddress;
        private System.Windows.Forms.TextBox txtIpAddress;
        private System.Windows.Forms.Label lblMask;
        private System.Windows.Forms.TextBox txtMask;
        private System.Windows.Forms.Label lblGateway;
        private System.Windows.Forms.TextBox txtGateway;
        private System.Windows.Forms.Label lblDnsMode;
        private System.Windows.Forms.FlowLayoutPanel flpDnsMode;
        private System.Windows.Forms.RadioButton rbDnsAuto;
        private System.Windows.Forms.RadioButton rbDnsManual;
        private System.Windows.Forms.Button btnDnsPreset;
        private System.Windows.Forms.Button btnDnsBenchmark;
        private System.Windows.Forms.Label lblDnsMain;
        private System.Windows.Forms.TextBox txtDnsMain;
        private System.Windows.Forms.Label lblDnsBackup;
        private System.Windows.Forms.TextBox txtDnsBackup;
        private System.Windows.Forms.Panel pnlSeparator;
        private System.Windows.Forms.Label lblMacCaption;
        private System.Windows.Forms.TextBox txtMac;
        private System.Windows.Forms.Label lblDhcpCaption;
        private System.Windows.Forms.TextBox txtDhcpServer;
        private System.Windows.Forms.Label lblIPv6Caption;
        private System.Windows.Forms.TextBox txtIPv6;
        private System.Windows.Forms.Panel pnlActions;
        private System.Windows.Forms.TableLayoutPanel tlpActions;
        private System.Windows.Forms.Button btnProfile;
        private System.Windows.Forms.Button btnDiagnose;
        private System.Windows.Forms.Button btnReport;
        private System.Windows.Forms.Button btnRevert;
        private System.Windows.Forms.Button btnApply;
        private System.Windows.Forms.Panel pnlStatus;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.ErrorProvider errorProvider;
        private System.Windows.Forms.ToolTip toolTip;
    }
}
