using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Management;
using System.Net;
using System.IO;

namespace IP_UpdateTest
{
    public partial class FrmMain : Form
    {
        public FrmMain()
        {
            InitializeComponent();
        }

        #region load事件
        private void FrmMain_Load_1(object sender, EventArgs e)
        {
            // 应用现代化主题
            ApplyModernTheme();

            cbxNetworkAdapter.DisplayMember = "Description";
            state = AdapaterState.EthernetWirelessUseing;
            BindAdapters();
        }

        /// <summary>
        /// 应用现代化 UI 主题
        /// </summary>
        private void ApplyModernTheme()
        {
            // 调整窗体尺寸以容纳更多功能
            this.ClientSize = new Size(420, 500);

            // 窗体基础样式
            UITheme.ApplyTheme(this);

            // 移除旧工具栏，添加自定义标题栏
            this.Controls.Remove(toolStrip1);
            var titleBar = UITheme.CreateTitleBar(this, "网络配置工具");
            this.Controls.Add(titleBar);

            // 添加工具栏面板 - 第一行
            var toolPanel1 = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 32,
                BackColor = UITheme.CardBackground,
                Padding = new Padding(UITheme.Padding, 4, UITheme.Padding, 0),
                WrapContents = false
            };

            var btnUsing = UITheme.CreateToolButton("正在使用", tsBtnUsingNetwork_Click);
            var btnDisable = UITheme.CreateToolButton("禁用所有", tsBtnDisableAdapters_Click);
            var btnEnable = UITheme.CreateToolButton("启用所有", tsBtnEnableAdapters_Click);
            var btnReport = UITheme.CreateToolButton("生成报表", tsBtnAllIPReport_Click);

            toolPanel1.Controls.AddRange(new Control[] { btnUsing, btnDisable, btnEnable, btnReport });
            this.Controls.Add(toolPanel1);

            // 添加工具栏面板 - 第二行（新功能）
            var toolPanel2 = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 32,
                BackColor = UITheme.CardBackground,
                Padding = new Padding(UITheme.Padding, 0, UITheme.Padding, 4),
                WrapContents = false
            };

            var btnDiagnose = UITheme.CreateToolButton("网络诊断", BtnDiagnose_Click);
            var btnProfile = UITheme.CreateToolButton("配置方案 ▼", BtnProfile_Click);
            btnProfile.BackColor = UITheme.Primary;
            btnProfile.ForeColor = Color.White;

            toolPanel2.Controls.AddRange(new Control[] { btnDiagnose, btnProfile });
            this.Controls.Add(toolPanel2);

            // 样式化所有标签
            foreach (Control ctrl in this.Controls)
            {
                if (ctrl is Label lbl && !(ctrl is LinkLabel))
                    UITheme.StyleLabel(lbl);
            }

            // 样式化输入控件
            UITheme.StyleComboBox(cbxNetworkAdapter);
            UITheme.StyleComboBox(cbxGetIPMethod);
            UITheme.StyleTextBox(txtIpAddress);
            UITheme.StyleTextBox(txtGetway);
            UITheme.StyleTextBox(txtDnsMain);
            UITheme.StyleTextBox(txtDnsBackup);
            UITheme.StyleTextBox(txtDhcpServer);
            UITheme.StyleTextBox(txtPhycilAddress);

            // 样式化按钮
            UITheme.StylePrimaryButton(btnGetIPAddress);
            UITheme.StyleSuccessButton(btnSetAutoIPAddress);

            // 调整控件位置（标题栏高度变化）
            AdjustControlPositions();

            // 创建 DNS 右键菜单
            CreateDnsContextMenu();
        }

        /// <summary>
        /// 调整控件位置以适应新标题栏
        /// </summary>
        private void AdjustControlPositions()
        {
            int offsetY = 80; // 标题栏 40 + 工具栏两行 64 - 原工具栏 25 ≈ 80
            foreach (Control ctrl in this.Controls)
            {
                if (ctrl is Panel) continue;
                if (ctrl is FlowLayoutPanel) continue;
                ctrl.Top += offsetY;
            }
        }
        #endregion

        #region 字段

        /// <summary>
        /// 网卡的index，用于改IP地址后，重新再次选中
        /// </summary>
        private int selectIndex = 0;
        /// <summary>
        /// 查询到指定状态网络适配器
        /// </summary>
        private AdapaterState state;
        /// <summary>
        /// 窗口拖动
        /// </summary>
        private Point offset;

        #endregion

        #region 方法
        /// <summary>
        /// 绑定Wireless,Ehternet类型 UP的配制器
        /// </summary>
        /// <returns></returns>
        private void BindEthernetWirelessAdaptersUP()
        {
            NetworkAdapterUtil util = new NetworkAdapterUtil();
            List<NetworkAdapter> lists = util.GetEthernetWirelessNetworkAdaptersUP();

            cbxNetworkAdapter.DataSource = lists; //重新绑定数据

            if (cbxNetworkAdapter != null && cbxNetworkAdapter.Items.Count > selectIndex)
            {
                cbxNetworkAdapter.SelectedIndex = selectIndex;
            }
        }

        /// <summary>
        /// 绑定所有适配器
        /// </summary>
        private void BindAllAdapters()
        {

            NetworkAdapterUtil util = new NetworkAdapterUtil();
            List<NetworkAdapter> lists = util.GetAllNetworkAdapters(); //得到所有适配器;

            cbxNetworkAdapter.DataSource = lists; //重新绑定数据

            if (cbxNetworkAdapter != null && cbxNetworkAdapter.Items.Count > selectIndex)
            {
                cbxNetworkAdapter.SelectedIndex = selectIndex;
            }
        }

        /// <summary>
        /// 绑定适配器,会根据state的值绑定
        /// </summary>
        private void BindAdapters()
        {

            if (state == AdapaterState.All)
            {
                BindAllAdapters();
            }
            else if (state == AdapaterState.EthernetWirelessUseing)
            {
                BindEthernetWirelessAdaptersUP();
            }
        }
        #endregion

        #region 网卡
        private void cbxNetworkAdapter_SelectedIndexChanged(object sender, EventArgs e)
        {

            NetworkAdapter adapter = cbxNetworkAdapter.SelectedItem as NetworkAdapter; //获取到选中的适配器
            txtIpAddress.Text = adapter.IpAddress;
            txtGetway.Text = adapter.Getway;
            txtMask.Text = adapter.Mask;
            txtPhycilAddress.Text = adapter.MacAddres;
            txtDnsMain.Text = adapter.DnsMain;
            txtDnsBackup.Text = adapter.DnsBackup;
            txtDhcpServer.Text = adapter.DhcpServer;
            if (adapter.IsDhcpEnabled)
            {
                cbxGetIPMethod.SelectedIndex = 0;
            }
            else
            {
                cbxGetIPMethod.SelectedIndex = 1;
            }

        }
        #endregion

        #region 设置IP

        private void btnGetIPAddress_Click(object sender, EventArgs e)
        {

            try
            {
                NetworkAdapter adapter = cbxNetworkAdapter.SelectedItem as NetworkAdapter;
                if (adapter == null) MessageBox.Show("请先选择适配器");
                selectIndex = cbxNetworkAdapter.SelectedIndex; //获取适配器的index,为了重新加载再选上这

                string ipAddress = txtIpAddress.Text.Trim(); //IP地址
                string subMask = txtMask.Text.Trim(); //子网掩码           
                string getWay = txtGetway.Text.Trim(); //网关             
                string dnsMain = txtDnsMain.Text.Trim(); //主DNS
                string dnsBackup = txtDnsBackup.Text.Trim(); //备用DNS
                string reslut = adapter.IsIPAddress(ipAddress, subMask, getWay, dnsMain, dnsBackup); //检查输入设置的IP地址，如果返回空，表示成功，否则就失败
                if (!string.IsNullOrEmpty(reslut))
                {
                    MessageBox.Show(reslut);
                    return;
                }

                if (!adapter.SetIPAddressAndSubMask(ipAddress, subMask))//设置IP地址和子网掩码
                {
                    MessageBox.Show("设置IP地址和子网掩码失败");
                    return;
                }

                if (!string.IsNullOrEmpty(getWay))
                {
                    if (!adapter.SetGetWayAddress(getWay)) //设置网关地址
                    {
                        MessageBox.Show("设置网关地址失败");
                        return;
                    }
                }

                if (!string.IsNullOrEmpty(dnsMain))
                {
                    if (!adapter.SetDNSAddress(dnsMain, dnsBackup)) //设置DNS地址;
                    {
                        MessageBox.Show("设置DNS失败");
                        return;
                    }
                }

                MessageBox.Show("设置IP地址成功");
                BindAdapters(); //绑定适配器

            }
            catch (Exception)
            {
                MessageBox.Show("Catch Exception!!!");
            }
        }
        #endregion

        #region  设置自动获取

        private void btnSetAutoIPAddress_Click(object sender, EventArgs e)
        {

            NetworkAdapter adapter = cbxNetworkAdapter.SelectedItem as NetworkAdapter; //获取到选中的适配器
            if (adapter == null) MessageBox.Show("请先选择适配器");
            selectIndex = cbxNetworkAdapter.SelectedIndex; //控件cbxNetworkAdapter选中的Index,用户重新加载再选中

            if (adapter.EnableDHCP()) //设置自动获取IP地址
            {
                MessageBox.Show("设置自动获取IP成功");
                BindAdapters();////绑定适配器
            }
            else
            {
                MessageBox.Show("设置自动获取IP失败");
            }

        }
        #endregion



        #region 事件
        /// <summary>
        /// 禁用所有链接
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void tsBtnDisableAdapters_Click(object sender, EventArgs e)
        {
            try
            {
                new NetworkAdapterUtil().DisableAllAdapters();
                MessageBox.Show("禁用所有网络连接成功");
                // BindAdapters(); //绑定适配器
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }

        }

        private void tsBtnUsingNetwork_Click(object sender, EventArgs e)
        {
            state = AdapaterState.EthernetWirelessUseing;
            BindAdapters();
        }
        /// <summary>
        /// 启用所有链接
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void tsBtnEnableAdapters_Click(object sender, EventArgs e)
        {

            try
            {
                new NetworkAdapterUtil().EnableAllAdapters();
                MessageBox.Show("启用所有网络连接成功");
                //  BindAdapters(); //绑定适配器

            }
            catch (Exception ex)
            {

                MessageBox.Show(ex.Message);
            }
        }

        private void tsBtnAllIPReport_Click(object sender, EventArgs e)
        {

            FrmInfo frmReport = new FrmInfo();
            frmReport.Show();

        }
        // 关闭
        private void toolStripLabel1_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        #endregion

        #region 新功能 - 网络诊断、配置方案、DNS 切换

        /// <summary>
        /// 创建 DNS 右键菜单
        /// </summary>
        private void CreateDnsContextMenu()
        {
            var menu = new ContextMenuStrip();
            foreach (var dns in ProfileManager.PresetDns)
            {
                var item = new ToolStripMenuItem(dns.Key);
                item.Click += (s, e) =>
                {
                    txtDnsMain.Text = dns.Value[0];
                    txtDnsBackup.Text = dns.Value[1];
                };
                menu.Items.Add(item);
            }
            txtDnsMain.ContextMenuStrip = menu;
            txtDnsBackup.ContextMenuStrip = menu;
        }

        /// <summary>
        /// 网络诊断
        /// </summary>
        private async void BtnDiagnose_Click(object sender, EventArgs e)
        {
            var btn = sender as Button;
            btn.Enabled = false;
            btn.Text = "诊断中...";

            try
            {
                string gateway = txtGetway.Text.Trim();
                string result = await NetworkTester.DiagnoseAsync(gateway);
                MessageBox.Show(result, "网络诊断", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            finally
            {
                btn.Text = "网络诊断";
                btn.Enabled = true;
            }
        }

        /// <summary>
        /// 配置方案菜单
        /// </summary>
        private void BtnProfile_Click(object sender, EventArgs e)
        {
            var btn = sender as Button;
            var menu = new ContextMenuStrip();

            // 保存当前配置
            menu.Items.Add("保存当前配置", null, (s, ev) => SaveCurrentProfile());
            menu.Items.Add(new ToolStripSeparator());

            // 已保存的配置方案
            var profiles = ProfileManager.Profiles;
            if (profiles.Count > 0)
            {
                foreach (var p in profiles)
                {
                    var item = new ToolStripMenuItem(p.Name + (p.IsDhcp ? " (DHCP)" : ""));
                    item.Click += (s, ev) => ApplyProfile(p);

                    // 右键删除
                    var deleteItem = new ToolStripMenuItem("删除");
                    deleteItem.Click += (s2, ev2) =>
                    {
                        if (MessageBox.Show($"确定删除配置方案 \"{p.Name}\"？", "确认", MessageBoxButtons.YesNo) == DialogResult.Yes)
                        {
                            ProfileManager.Remove(p.Name);
                            MessageBox.Show("已删除");
                        }
                    };
                    item.DropDownItems.Add(deleteItem);

                    menu.Items.Add(item);
                }
                menu.Items.Add(new ToolStripSeparator());
            }

            // 导入导出
            menu.Items.Add("导出配置", null, (s, ev) => ExportProfiles());
            menu.Items.Add("导入配置", null, (s, ev) => ImportProfiles());

            menu.Show(btn, new Point(0, btn.Height));
        }

        /// <summary>
        /// 保存当前配置为方案
        /// </summary>
        private void SaveCurrentProfile()
        {
            string name = Microsoft.VisualBasic.Interaction.InputBox("请输入配置方案名称：", "保存配置", "");
            if (string.IsNullOrWhiteSpace(name)) return;

            var profile = new IpProfile
            {
                Name = name,
                IpAddress = txtIpAddress.Text.Trim(),
                SubnetMask = txtMask.Text.Trim(),
                Gateway = txtGetway.Text.Trim(),
                DnsMain = txtDnsMain.Text.Trim(),
                DnsBackup = txtDnsBackup.Text.Trim(),
                IsDhcp = cbxGetIPMethod.SelectedIndex == 0
            };

            ProfileManager.Add(profile);
            MessageBox.Show($"配置方案 \"{name}\" 已保存", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>
        /// 应用配置方案
        /// </summary>
        private void ApplyProfile(IpProfile profile)
        {
            if (profile.IsDhcp)
            {
                btnSetAutoIPAddress_Click(null, null);
            }
            else
            {
                txtIpAddress.Text = profile.IpAddress;
                txtMask.Text = profile.SubnetMask;
                txtGetway.Text = profile.Gateway;
                txtDnsMain.Text = profile.DnsMain;
                txtDnsBackup.Text = profile.DnsBackup;
                cbxGetIPMethod.SelectedIndex = 1;

                if (MessageBox.Show("是否立即应用此配置？", "确认", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    btnGetIPAddress_Click(null, null);
                }
            }
        }

        /// <summary>
        /// 导出配置
        /// </summary>
        private void ExportProfiles()
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Filter = "JSON 文件|*.json";
                dialog.FileName = "ip_profiles.json";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        ProfileManager.Export(dialog.FileName);
                        MessageBox.Show("导出成功", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("导出失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        /// <summary>
        /// 导入配置
        /// </summary>
        private void ImportProfiles()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = "JSON 文件|*.json";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        int count = ProfileManager.Import(dialog.FileName);
                        MessageBox.Show($"成功导入 {count} 个配置方案", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("导入失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        #endregion

        #region 点击窗口中任意位置拖动窗口

        /// <summary>
        /// 鼠标按下事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FrmMain_MouseDown(object sender, MouseEventArgs e)
        {
            if (MouseButtons.Left != e.Button) return;

            Point cur = this.PointToScreen(e.Location);
            offset = new Point(cur.X - this.Left, cur.Y - this.Top);
        }
        /// <summary>
        /// 鼠标移动事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FrmMain_MouseMove(object sender, MouseEventArgs e)
        {
            if (MouseButtons.Left != e.Button) return;

            Point cur = MousePosition;
            this.Location = new Point(cur.X - offset.X, cur.Y - offset.Y);
        }
        #endregion









        /// 获取IP方式
        private void cbxGetIPMethod_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void tsBtnDisplayAllNetwork_Click(object sender, EventArgs e)
        {

        }
        //适配器
        enum AdapaterState
        {
            All,
            EthernetWirelessUseing
        }

        //标签, 禁用与启用连接
        enum tagTemp
        {
            on = 1,
            off = 0
        }
    }
}
