using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace IP_UpdateTest
{
    public partial class FrmInfo : Form
    {
        public FrmInfo()
        {
            InitializeComponent();
        }



        private void FrmReport_Load(object sender, EventArgs e)
        {
            cbxReoprt.SelectedIndex = 0;
        }

        private void cbxReoprt_SelectedIndexChanged(object sender, EventArgs e)
        {
            string report = "无适配器报表";
            if (cbxReoprt.SelectedIndex == 0)
            {
                NetworkAdapterUtil adapter = new NetworkAdapterUtil();
                report = adapter.ReportByAdapters(adapter.GetEthernetWirelessNetworkAdaptersUP());
            }
            else if (cbxReoprt.SelectedIndex == 1)
            {
                NetworkAdapterUtil adapter = new NetworkAdapterUtil();
                report = adapter.ReportByAdapters(adapter.GetAllNetworkAdapters());
            }
            txtReport.Text = report;
            txtReport.SelectionStart = 0;


        }


        /// <summary>
        /// load事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FrmInfo_Load(object sender, EventArgs e)
        {
            cbxReoprt.SelectedIndex = 0;

        }


        private void cbxReoprt_SelectedIndexChanged_1(object sender, EventArgs e)
        {

            string report = "无适配器报表";
            if (cbxReoprt.SelectedIndex == 0)
            {
                NetworkAdapterUtil adapter = new NetworkAdapterUtil();
                report = adapter.ReportByAdapters(adapter.GetEthernetWirelessNetworkAdaptersUP());
            }
            else if (cbxReoprt.SelectedIndex == 1)
            {
                NetworkAdapterUtil adapter = new NetworkAdapterUtil();
                report = adapter.ReportByAdapters(adapter.GetAllNetworkAdapters());
            }
            txtReport.Text = report;
            txtReport.SelectionStart = 0;
        }

        private void label2_Click(object sender, EventArgs e)
        {
            this.Close();

        }


        #region 窗口移动事件
        /// <summary>
        /// 窗口移动事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>

        private Point offset;

        private void FrmInfo_MouseDown(object sender, MouseEventArgs e)
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
        private void FrmInfo_MouseMove(object sender, MouseEventArgs e)
        {

            if (MouseButtons.Left != e.Button) return;

            Point cur = MousePosition;
            this.Location = new Point(cur.X - offset.X, cur.Y - offset.Y);
        }
        #endregion

        //超链接   www.kekemao.top
        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start("iexplore.exe", "http://kekemao.top");
        }


        //打开kekemao.top
        //private void label3_Click(object sender, EventArgs e)
        //{
        //    System.Diagnostics.Process.Start("iexplore.exe", "http://kekemao.top");
        //}

    }
}
