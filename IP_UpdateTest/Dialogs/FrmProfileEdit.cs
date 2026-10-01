using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using IP_UpdateTest.Core;

namespace IP_UpdateTest
{
    /// <summary>
    /// 新建或编辑配置方案
    /// </summary>
    public class FrmProfileEdit : DialogBase
    {
        private readonly TextBox txtName = new TextBox();
        private readonly RadioButton rbDhcp = new RadioButton { Text = "自动获取 (DHCP)", AutoSize = true, Checked = true };
        private readonly RadioButton rbStatic = new RadioButton { Text = "手动设置", AutoSize = true };
        private readonly TextBox txtIp = new TextBox();
        private readonly TextBox txtMask = new TextBox();
        private readonly TextBox txtGateway = new TextBox();
        private readonly RadioButton rbDnsAuto = new RadioButton { Text = "自动获取", AutoSize = true, Checked = true };
        private readonly RadioButton rbDnsManual = new RadioButton { Text = "手动设置", AutoSize = true };
        private readonly TextBox txtDnsMain = new TextBox();
        private readonly TextBox txtDnsBackup = new TextBox();
        private readonly ComboBox cbxAdapter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly ErrorProvider errorProvider = new ErrorProvider { BlinkStyle = ErrorBlinkStyle.NeverBlink };

        /// <summary>
        /// 编辑前的名称；新建时为 null
        /// </summary>
        private readonly string originalName;
        private readonly DateTime createTime;

        /// <summary>
        /// 保存后的方案
        /// </summary>
        public IpProfile Profile { get; private set; }

        public FrmProfileEdit(IpProfile profile, IEnumerable<NetworkAdapter> adapters)
            : base(profile == null ? "新建配置方案" : "编辑配置方案", 430, 480)
        {
            originalName = profile == null ? null : profile.Name;
            createTime = profile == null ? DateTime.Now : profile.CreateTime;
            errorProvider.ContainerControl = this;

            TableLayoutPanel table = CreateFormTable();
            AddRow(table, "名称", txtName);
            AddRow(table, "IP 获取", ChoicePanel(rbDhcp, rbStatic));
            AddRow(table, "IP 地址", txtIp);
            AddRow(table, "子网掩码", txtMask);
            AddRow(table, "默认网关", txtGateway);
            AddRow(table, "DNS 获取", ChoicePanel(rbDnsAuto, rbDnsManual));
            AddRow(table, "首选 DNS", txtDnsMain);
            AddRow(table, "备用 DNS", txtDnsBackup);
            AddRow(table, "绑定网卡", cbxAdapter);
            var hint = new Label { Text = "绑定后总是应用到该网卡；不绑定则应用到当前选中的网卡", AutoSize = true };
            UITheme.StyleLabel(hint);
            AddRow(table, null, hint, 24);
            ContentPanel.Controls.Add(table);

            AcceptButton = AddButton("保存", true, (s, e) => Save());
            CancelButton = AddButton("取消", false, (s, e) => DialogResult = DialogResult.Cancel);
            FinishLayout();

            foreach (TextBox box in new[] { txtName, txtIp, txtMask, txtGateway, txtDnsMain, txtDnsBackup })
                UITheme.StyleTextBox(box);
            foreach (RadioButton choice in new[] { rbDhcp, rbStatic, rbDnsAuto, rbDnsManual })
            {
                UITheme.StyleChoice(choice);
                choice.CheckedChanged += (s, e) => UpdateStates();
            }
            UITheme.StyleComboBox(cbxAdapter);

            LoadAdapters(adapters, profile == null ? null : profile.AdapterMac);
            LoadProfile(profile);
            UpdateStates();
        }

        private static FlowLayoutPanel ChoicePanel(RadioButton first, RadioButton second)
        {
            var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            first.Margin = new Padding(0, 2, 20, 2);
            second.Margin = new Padding(0, 2, 0, 2);
            panel.Controls.Add(first);
            panel.Controls.Add(second);
            return panel;
        }

        private void LoadAdapters(IEnumerable<NetworkAdapter> adapters, string boundMac)
        {
            cbxAdapter.Items.Add(new AdapterChoice(null, "不绑定"));
            foreach (NetworkAdapter adapter in adapters.Where(a => a.MacAddressText.Length > 0))
                cbxAdapter.Items.Add(new AdapterChoice(adapter.MacAddressText, $"{adapter.Name}（{adapter.MacAddressText}）"));

            // 绑定的网卡不在列表中（如已拔出）时也保留，避免保存时丢掉绑定
            if (!string.IsNullOrEmpty(boundMac) && !cbxAdapter.Items.Cast<AdapterChoice>().Any(c => c.Mac == boundMac))
                cbxAdapter.Items.Add(new AdapterChoice(boundMac, $"未连接的网卡（{boundMac}）"));

            AdapterChoice selected = cbxAdapter.Items.Cast<AdapterChoice>().FirstOrDefault(c => c.Mac == boundMac);
            cbxAdapter.SelectedItem = selected ?? cbxAdapter.Items[0];
        }

        private void LoadProfile(IpProfile profile)
        {
            if (profile == null) return;

            txtName.Text = profile.Name;
            IpConfigRequest request = profile.ToRequest();
            rbDhcp.Checked = request.UseDhcp;
            rbStatic.Checked = !request.UseDhcp;
            txtIp.Text = profile.IpAddress;
            txtMask.Text = profile.SubnetMask;
            txtGateway.Text = profile.Gateway;
            rbDnsAuto.Checked = request.UseDhcp && request.UseDhcpDns;
            rbDnsManual.Checked = !rbDnsAuto.Checked;
            txtDnsMain.Text = request.DnsServers.Length > 0 ? request.DnsServers[0] : "";
            txtDnsBackup.Text = request.DnsServers.Length > 1 ? request.DnsServers[1] : "";
        }

        private void UpdateStates()
        {
            bool isStatic = rbStatic.Checked;
            if (isStatic && !rbDnsManual.Checked) rbDnsManual.Checked = true; // 静态 IP 时 DNS 只能手动设置

            txtIp.Enabled = txtMask.Enabled = txtGateway.Enabled = isStatic;
            rbDnsAuto.Enabled = !isStatic;
            txtDnsMain.Enabled = txtDnsBackup.Enabled = rbDnsManual.Checked;
            foreach (TextBox box in new[] { txtIp, txtMask, txtGateway, txtDnsMain, txtDnsBackup })
                box.BackColor = box.Enabled ? UITheme.InputBackground : UITheme.HoverBackground;
        }

        private IpProfile BuildProfile()
        {
            bool isDhcp = rbDhcp.Checked;
            bool manualDns = rbDnsManual.Checked;
            return new IpProfile
            {
                Name = txtName.Text.Trim(),
                IsDhcp = isDhcp,
                IpAddress = isDhcp ? "" : txtIp.Text.Trim(),
                SubnetMask = isDhcp ? "" : txtMask.Text.Trim(),
                Gateway = isDhcp ? "" : txtGateway.Text.Trim(),
                ManualDns = manualDns,
                DnsMain = manualDns ? txtDnsMain.Text.Trim() : "",
                DnsBackup = manualDns ? txtDnsBackup.Text.Trim() : "",
                AdapterMac = ((AdapterChoice)cbxAdapter.SelectedItem).Mac,
                CreateTime = createTime
            };
        }

        private void Save()
        {
            errorProvider.Clear();
            IpProfile profile = BuildProfile();
            if (profile.Name.Length == 0)
            {
                errorProvider.SetError(txtName, "请填写名称");
                txtName.Focus();
                return;
            }

            List<ValidationError> errors = profile.ToRequest().Validate();
            foreach (ValidationError error in errors)
            {
                TextBox box = FieldOf(error.Field);
                if (errorProvider.GetError(box).Length == 0) errorProvider.SetError(box, error.Message);
            }
            if (errors.Count > 0)
            {
                FieldOf(errors[0].Field).Focus();
                return;
            }

            // 新建时与已有方案重名：确认后覆盖
            if (originalName == null && ProfileManager.Find(profile.Name) != null
                && !UITheme.Confirm($"已存在名为“{profile.Name}”的配置方案，是否覆盖？", "保存配置方案"))
                return;

            try
            {
                if (originalName == null) ProfileManager.Add(profile);
                else ProfileManager.Update(originalName, profile);
            }
            catch (ArgumentException ex)
            {
                errorProvider.SetError(txtName, ex.Message);
                return;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                UITheme.ShowMessage("保存失败：" + ex.Message, "保存配置方案", MessageBoxIcon.Error);
                return;
            }

            Profile = profile;
            DialogResult = DialogResult.OK;
        }

        private TextBox FieldOf(IpField field)
        {
            switch (field)
            {
                case IpField.IpAddress: return txtIp;
                case IpField.SubnetMask: return txtMask;
                case IpField.Gateway: return txtGateway;
                case IpField.DnsMain: return txtDnsMain;
                default: return txtDnsBackup;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) errorProvider.Dispose();
            base.Dispose(disposing);
        }

        /// <summary>
        /// 绑定网卡下拉框的选项
        /// </summary>
        private sealed class AdapterChoice
        {
            public AdapterChoice(string mac, string text)
            {
                Mac = mac;
                Text = text;
            }

            public string Mac { get; private set; }

            public string Text { get; private set; }

            public override string ToString()
            {
                return Text;
            }
        }
    }
}
