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
        private void FrmInfo_Load(object sender, EventArgs e)
        {
            ApplyModernTheme();
            cbxReoprt.SelectedIndex = 0;
        }

        /// <summary>
        /// 应用现代化 UI 主题
        /// </summary>
        private void ApplyModernTheme()
        {
            // 窗体基础样式
            UITheme.ApplyTheme(this);
            this.Opacity = 1.0;

            // 移除旧关闭按钮
            this.Controls.Remove(label2);

            // 添加自定义标题栏
            var titleBar = UITheme.CreateTitleBar(this, "网卡信息报表");
            this.Controls.Add(titleBar);

            // 样式化控件
            UITheme.StyleComboBox(cbxReoprt);
            UITheme.StyleLabel(label1);

            // 样式化报表文本框
            txtReport.BackColor = UITheme.CardBackground;
            txtReport.ForeColor = UITheme.TextPrimary;
            txtReport.Font = new Font("Consolas", 9F);
            txtReport.BorderStyle = BorderStyle.FixedSingle;

            // 调整控件位置
            int offsetY = 15;
            label1.Top += offsetY;
            cbxReoprt.Top += offsetY;
            txtReport.Top += offsetY;
            linkLabel1.Top += offsetY;
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
        private Point offset;

        private void FrmInfo_MouseDown(object sender, MouseEventArgs e)
        {
            // 由标题栏处理拖动
        }

        private void FrmInfo_MouseMove(object sender, MouseEventArgs e)
        {
            // 由标题栏处理拖动
        }
        #endregion

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "http://kekemao.top",
                    UseShellExecute = true
                });
            }
            catch { }
        }


        //打开kekemao.top
        //private void label3_Click(object sender, EventArgs e)
        //{
        //    System.Diagnostics.Process.Start("iexplore.exe", "http://kekemao.top");
        //}

    }
}
