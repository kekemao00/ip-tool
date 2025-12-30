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

            tagTemp temp = tagTemp.on;
            this.tsBtnUsingNetwork.BackColor = Color.SpringGreen;
            cbxNetworkAdapter.DisplayMember = "Description";
            state = AdapaterState.EthernetWirelessUseing;
            BindAdapters();

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

            tsBtnUsingNetwork.BackColor = Color.SkyBlue;
            //tsBtnDisplayAllNetwork.BackColor = SystemColors.Control;


            state = AdapaterState.EthernetWirelessUseing;

            BindAdapters();  //重新获取适配器信息并绑定
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
