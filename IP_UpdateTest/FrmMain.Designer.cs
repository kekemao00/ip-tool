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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FrmMain));
            this.tsBtnAllIPReport = new System.Windows.Forms.ToolStripButton();
            this.tsBtnEnableAdapters = new System.Windows.Forms.ToolStripButton();
            this.tsBtnUsingNetwork = new System.Windows.Forms.ToolStripButton();
            this.toolStrip1 = new System.Windows.Forms.ToolStrip();
            this.tsBtnDisableAdapters = new System.Windows.Forms.ToolStripButton();
            this.toolStripLabel1 = new System.Windows.Forms.ToolStripLabel();
            this.cbxGetIPMethod = new System.Windows.Forms.ComboBox();
            this.cbxNetworkAdapter = new System.Windows.Forms.ComboBox();
            this.txtDhcpServer = new System.Windows.Forms.TextBox();
            this.txtPhycilAddress = new System.Windows.Forms.TextBox();
            this.txtDnsBackup = new System.Windows.Forms.TextBox();
            this.label8 = new System.Windows.Forms.Label();
            this.label9 = new System.Windows.Forms.Label();
            this.txtDnsMain = new System.Windows.Forms.TextBox();
            this.label7 = new System.Windows.Forms.Label();
            this.txtGetway = new System.Windows.Forms.TextBox();
            this.label5 = new System.Windows.Forms.Label();
            this.txtIpAddress = new System.Windows.Forms.TextBox();
            this.label4 = new System.Windows.Forms.Label();
            this.txtMask = new System.Windows.Forms.MaskedTextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label6 = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.btnSetAutoIPAddress = new System.Windows.Forms.Button();
            this.btnGetIPAddress = new System.Windows.Forms.Button();
            this.toolStrip1.SuspendLayout();
            this.SuspendLayout();
            // 
            // tsBtnAllIPReport
            // 
            this.tsBtnAllIPReport.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsBtnAllIPReport.Image = ((System.Drawing.Image)(resources.GetObject("tsBtnAllIPReport.Image")));
            this.tsBtnAllIPReport.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.tsBtnAllIPReport.Name = "tsBtnAllIPReport";
            this.tsBtnAllIPReport.Size = new System.Drawing.Size(60, 22);
            this.tsBtnAllIPReport.Text = "生成报表";
            this.tsBtnAllIPReport.Click += new System.EventHandler(this.tsBtnAllIPReport_Click);
            // 
            // tsBtnEnableAdapters
            // 
            this.tsBtnEnableAdapters.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsBtnEnableAdapters.Image = ((System.Drawing.Image)(resources.GetObject("tsBtnEnableAdapters.Image")));
            this.tsBtnEnableAdapters.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.tsBtnEnableAdapters.Name = "tsBtnEnableAdapters";
            this.tsBtnEnableAdapters.Size = new System.Drawing.Size(84, 22);
            this.tsBtnEnableAdapters.Text = "启用所有连接";
            this.tsBtnEnableAdapters.Click += new System.EventHandler(this.tsBtnEnableAdapters_Click);
            // 
            // tsBtnUsingNetwork
            // 
            this.tsBtnUsingNetwork.BackColor = System.Drawing.SystemColors.Control;
            this.tsBtnUsingNetwork.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsBtnUsingNetwork.Image = ((System.Drawing.Image)(resources.GetObject("tsBtnUsingNetwork.Image")));
            this.tsBtnUsingNetwork.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.tsBtnUsingNetwork.Name = "tsBtnUsingNetwork";
            this.tsBtnUsingNetwork.Size = new System.Drawing.Size(108, 22);
            this.tsBtnUsingNetwork.Text = "正在使用网络连接";
            this.tsBtnUsingNetwork.Click += new System.EventHandler(this.tsBtnUsingNetwork_Click);
            // 
            // toolStrip1
            // 
            this.toolStrip1.BackgroundImage = global::IP_UpdateTest.Properties.Resources.skyBule;
            this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tsBtnUsingNetwork,
            this.tsBtnDisableAdapters,
            this.tsBtnEnableAdapters,
            this.tsBtnAllIPReport,
            this.toolStripLabel1});
            this.toolStrip1.Location = new System.Drawing.Point(0, 0);
            this.toolStrip1.Name = "toolStrip1";
            this.toolStrip1.RenderMode = System.Windows.Forms.ToolStripRenderMode.System;
            this.toolStrip1.Size = new System.Drawing.Size(379, 25);
            this.toolStrip1.TabIndex = 32;
            this.toolStrip1.Text = "toolStrip1";
            // 
            // tsBtnDisableAdapters
            // 
            this.tsBtnDisableAdapters.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsBtnDisableAdapters.Image = ((System.Drawing.Image)(resources.GetObject("tsBtnDisableAdapters.Image")));
            this.tsBtnDisableAdapters.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.tsBtnDisableAdapters.Name = "tsBtnDisableAdapters";
            this.tsBtnDisableAdapters.Size = new System.Drawing.Size(84, 22);
            this.tsBtnDisableAdapters.Text = "禁用所有连接";
            this.tsBtnDisableAdapters.Click += new System.EventHandler(this.tsBtnDisableAdapters_Click);
            // 
            // toolStripLabel1
            // 
            this.toolStripLabel1.BackColor = System.Drawing.SystemColors.ControlDark;
            this.toolStripLabel1.ForeColor = System.Drawing.Color.Red;
            this.toolStripLabel1.Name = "toolStripLabel1";
            this.toolStripLabel1.Size = new System.Drawing.Size(32, 17);
            this.toolStripLabel1.Text = "关闭";
            this.toolStripLabel1.Click += new System.EventHandler(this.toolStripLabel1_Click);
            // 
            // cbxGetIPMethod
            // 
            this.cbxGetIPMethod.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cbxGetIPMethod.Enabled = false;
            this.cbxGetIPMethod.FormattingEnabled = true;
            this.cbxGetIPMethod.Items.AddRange(new object[] {
            "自动",
            "手动"});
            this.cbxGetIPMethod.Location = new System.Drawing.Point(106, 79);
            this.cbxGetIPMethod.Name = "cbxGetIPMethod";
            this.cbxGetIPMethod.Size = new System.Drawing.Size(248, 20);
            this.cbxGetIPMethod.TabIndex = 1;
            this.cbxGetIPMethod.Tag = "1";
            this.cbxGetIPMethod.SelectedIndexChanged += new System.EventHandler(this.cbxGetIPMethod_SelectedIndexChanged);
            // 
            // cbxNetworkAdapter
            // 
            this.cbxNetworkAdapter.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cbxNetworkAdapter.FormattingEnabled = true;
            this.cbxNetworkAdapter.Location = new System.Drawing.Point(106, 47);
            this.cbxNetworkAdapter.Name = "cbxNetworkAdapter";
            this.cbxNetworkAdapter.Size = new System.Drawing.Size(249, 20);
            this.cbxNetworkAdapter.TabIndex = 0;
            this.cbxNetworkAdapter.Tag = "0";
            this.cbxNetworkAdapter.SelectedIndexChanged += new System.EventHandler(this.cbxNetworkAdapter_SelectedIndexChanged);
            // 
            // txtDhcpServer
            // 
            this.txtDhcpServer.Location = new System.Drawing.Point(106, 307);
            this.txtDhcpServer.Name = "txtDhcpServer";
            this.txtDhcpServer.ReadOnly = true;
            this.txtDhcpServer.Size = new System.Drawing.Size(248, 21);
            this.txtDhcpServer.TabIndex = 8;
            this.txtDhcpServer.Tag = "9";
            // 
            // txtPhycilAddress
            // 
            this.txtPhycilAddress.Location = new System.Drawing.Point(106, 344);
            this.txtPhycilAddress.Name = "txtPhycilAddress";
            this.txtPhycilAddress.ReadOnly = true;
            this.txtPhycilAddress.Size = new System.Drawing.Size(248, 21);
            this.txtPhycilAddress.TabIndex = 9;
            this.txtPhycilAddress.Tag = "10";
            // 
            // txtDnsBackup
            // 
            this.txtDnsBackup.Location = new System.Drawing.Point(106, 270);
            this.txtDnsBackup.Name = "txtDnsBackup";
            this.txtDnsBackup.Size = new System.Drawing.Size(248, 21);
            this.txtDnsBackup.TabIndex = 7;
            this.txtDnsBackup.Tag = "8";
            // 
            // label8
            // 
            this.label8.AutoSize = true;
            this.label8.Location = new System.Drawing.Point(28, 310);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(65, 12);
            this.label8.TabIndex = 18;
            this.label8.Text = "DHCP服务器";
            // 
            // label9
            // 
            this.label9.AutoSize = true;
            this.label9.Location = new System.Drawing.Point(28, 82);
            this.label9.Name = "label9";
            this.label9.Size = new System.Drawing.Size(65, 12);
            this.label9.TabIndex = 19;
            this.label9.Text = "获取IP方式";
            // 
            // txtDnsMain
            // 
            this.txtDnsMain.Location = new System.Drawing.Point(106, 231);
            this.txtDnsMain.Name = "txtDnsMain";
            this.txtDnsMain.Size = new System.Drawing.Size(248, 21);
            this.txtDnsMain.TabIndex = 6;
            this.txtDnsMain.Tag = "7";
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.Location = new System.Drawing.Point(40, 344);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(53, 12);
            this.label7.TabIndex = 21;
            this.label7.Text = "物理地址";
            // 
            // txtGetway
            // 
            this.txtGetway.Location = new System.Drawing.Point(106, 192);
            this.txtGetway.Name = "txtGetway";
            this.txtGetway.Size = new System.Drawing.Size(248, 21);
            this.txtGetway.TabIndex = 5;
            this.txtGetway.Tag = "6";
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(40, 277);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(47, 12);
            this.label5.TabIndex = 22;
            this.label5.Text = "备用DNS";
            // 
            // txtIpAddress
            // 
            this.txtIpAddress.AutoCompleteSource = System.Windows.Forms.AutoCompleteSource.FileSystem;
            this.txtIpAddress.Location = new System.Drawing.Point(106, 114);
            this.txtIpAddress.Name = "txtIpAddress";
            this.txtIpAddress.Size = new System.Drawing.Size(162, 21);
            this.txtIpAddress.TabIndex = 2;
            this.txtIpAddress.Tag = "2";
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(40, 238);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(35, 12);
            this.label4.TabIndex = 20;
            this.label4.Text = "主DNS";
            // 
            // txtMask
            // 
            this.txtMask.Location = new System.Drawing.Point(106, 153);
            this.txtMask.Name = "txtMask";
            this.txtMask.PromptChar = ' ';
            this.txtMask.Size = new System.Drawing.Size(248, 21);
            this.txtMask.TabIndex = 4;
            this.txtMask.Tag = "5";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(40, 199);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(29, 12);
            this.label3.TabIndex = 17;
            this.label3.Text = "网关";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(40, 160);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(53, 12);
            this.label2.TabIndex = 16;
            this.label2.Text = "子网掩码";
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(40, 50);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(29, 12);
            this.label6.TabIndex = 15;
            this.label6.Text = "网卡";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(40, 121);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(41, 12);
            this.label1.TabIndex = 14;
            this.label1.Text = "IP地址";
            // 
            // btnSetAutoIPAddress
            // 
            this.btnSetAutoIPAddress.Location = new System.Drawing.Point(106, 385);
            this.btnSetAutoIPAddress.Name = "btnSetAutoIPAddress";
            this.btnSetAutoIPAddress.Size = new System.Drawing.Size(248, 23);
            this.btnSetAutoIPAddress.TabIndex = 11;
            this.btnSetAutoIPAddress.Tag = "12";
            this.btnSetAutoIPAddress.Text = "设置为自动获取";
            this.btnSetAutoIPAddress.UseVisualStyleBackColor = true;
            this.btnSetAutoIPAddress.Click += new System.EventHandler(this.btnSetAutoIPAddress_Click);
            // 
            // btnGetIPAddress
            // 
            this.btnGetIPAddress.Location = new System.Drawing.Point(274, 112);
            this.btnGetIPAddress.Name = "btnGetIPAddress";
            this.btnGetIPAddress.Size = new System.Drawing.Size(81, 23);
            this.btnGetIPAddress.TabIndex = 10;
            this.btnGetIPAddress.Tag = "11";
            this.btnGetIPAddress.Text = "设置IP";
            this.btnGetIPAddress.UseVisualStyleBackColor = true;
            this.btnGetIPAddress.Click += new System.EventHandler(this.btnGetIPAddress_Click);
            // 
            // FrmMain
            // 
            this.AcceptButton = this.btnGetIPAddress;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.SkyBlue;
            this.ClientSize = new System.Drawing.Size(379, 439);
            this.Controls.Add(this.toolStrip1);
            this.Controls.Add(this.cbxGetIPMethod);
            this.Controls.Add(this.cbxNetworkAdapter);
            this.Controls.Add(this.txtDhcpServer);
            this.Controls.Add(this.txtPhycilAddress);
            this.Controls.Add(this.txtDnsBackup);
            this.Controls.Add(this.label8);
            this.Controls.Add(this.label9);
            this.Controls.Add(this.txtDnsMain);
            this.Controls.Add(this.label7);
            this.Controls.Add(this.txtGetway);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.txtIpAddress);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.txtMask);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label6);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.btnSetAutoIPAddress);
            this.Controls.Add(this.btnGetIPAddress);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "FrmMain";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "本地IP地址修改工具";
            this.Load += new System.EventHandler(this.FrmMain_Load_1);
            this.MouseDown += new System.Windows.Forms.MouseEventHandler(this.FrmMain_MouseDown);
            this.MouseMove += new System.Windows.Forms.MouseEventHandler(this.FrmMain_MouseMove);
            this.toolStrip1.ResumeLayout(false);
            this.toolStrip1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ToolStripButton tsBtnAllIPReport;
        private System.Windows.Forms.ToolStripButton tsBtnEnableAdapters;
        private System.Windows.Forms.ToolStripButton tsBtnUsingNetwork;
        private System.Windows.Forms.ToolStrip toolStrip1;
        private System.Windows.Forms.ToolStripButton tsBtnDisableAdapters;
        private System.Windows.Forms.ComboBox cbxGetIPMethod;
        private System.Windows.Forms.ComboBox cbxNetworkAdapter;
        private System.Windows.Forms.TextBox txtDhcpServer;
        private System.Windows.Forms.TextBox txtPhycilAddress;
        private System.Windows.Forms.TextBox txtDnsBackup;
        private System.Windows.Forms.Label label8;
        private System.Windows.Forms.Label label9;
        private System.Windows.Forms.TextBox txtDnsMain;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.TextBox txtGetway;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.TextBox txtIpAddress;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.MaskedTextBox txtMask;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Button btnSetAutoIPAddress;
        private System.Windows.Forms.Button btnGetIPAddress;
        private System.Windows.Forms.ToolStripLabel toolStripLabel1;

    }
}

