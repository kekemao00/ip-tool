using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Windows.Forms;
using IP_UpdateTest.Core;
using IP_UpdateTest.Ui;

namespace IP_UpdateTest
{
    /// <summary>
    /// 配置卡片的 IPv6 页：与 IPv4 页切换显示，各自记录有没有改动，应用时只改动过的那一个
    /// </summary>
    public partial class FrmMain
    {
        private readonly ContextMenuStrip dns6PresetMenu = Menus.Create();

        /// <summary>
        /// IPv6 页“自动获取”时显示的状态
        /// </summary>
        private Ipv6StatusSummary ipv6Status = new Ipv6StatusSummary();

        /// <summary>
        /// IPv4 页有尚未应用的修改
        /// </summary>
        private bool ipv4Dirty;

        /// <summary>
        /// IPv6 页有尚未应用的修改
        /// </summary>
        private bool ipv6Dirty;

        private bool IsDirty
        {
            get { return ipv4Dirty || ipv6Dirty; }
        }

        private bool IsIpv6Page
        {
            get { return segFamily.SelectedIndex == 1; }
        }

        private FieldBox[] Ipv6Fields
        {
            get { return new[] { txt6Address, txt6Prefix, txt6Gateway, txt6DnsMain, txt6DnsBackup }; }
        }

        private bool IsManualIpv6
        {
            get { return seg6Mode.SelectedIndex == 1; }
        }

        private bool IsManualIpv6Dns
        {
            get { return seg6DnsMode.SelectedIndex == 1; }
        }

        private void WireIpv6Events()
        {
            segFamily.SelectedIndexChanged += (s, e) => ShowPage();
            seg6Mode.SelectedIndexChanged += Ipv6Mode_Changed;
            seg6DnsMode.SelectedIndexChanged += (s, e) => OnIpv6ModeChanged();
            // 先拆开“地址/前缀”再检查格式
            txt6Address.Box.Leave += (s, e) => NormalizeIpv6CidrInput();
            foreach (FieldBox box in Ipv6Fields)
            {
                FieldBox field = box;
                field.TextChanged += (s, e) =>
                {
                    if (isLoadingFields) return;
                    field.SetError(null);
                    MarkDirty(true);
                };
                field.Box.Leave += (s, e) => ValidateIpv6Field(field);
            }

            txt6Address.Placeholder = "如 2001:db8::10，可带 /64";
            txt6Prefix.Placeholder = "通常为 64";
            txt6Gateway.Placeholder = "如 fe80::1，可留空";
            txt6DnsMain.Placeholder = "如 2400:3200::1";
            txt6DnsBackup.Placeholder = "可留空";
            ShowPage();
        }

        /// <summary>
        /// 切换 IPv4 / IPv6 页：两页的控件位置相同，只换显示和行标题
        /// </summary>
        private void ShowPage()
        {
            bool v6 = IsIpv6Page;
            foreach (Control c in new Control[] { segIpMode, txtIpAddress, txtMask, txtGateway, segDnsMode, txtDnsMain, txtDnsBackup, btnDnsBenchmark })
                c.Visible = !v6;
            foreach (Control c in new Control[] { seg6Mode, seg6DnsMode, txt6DnsMain, txt6DnsBackup })
                c.Visible = v6;
            lblIpAddress.Text = v6 ? "IPv6 地址" : "IP 地址";
            lblMask.Text = v6 ? "前缀长度" : "子网掩码";
            InkToolTip.Shared.SetToolTip(btnDnsPreset, v6 ? "选择常用公共 DNS 的 IPv6 地址" : "选择常用公共 DNS");
            UpdateIpv6View();
            UpdateControlStates();

            NetworkAdapter adapter = SelectedAdapter;
            if (v6 && adapter != null && adapter.CanConfigure && !isInitializing)
            {
                string reason = AdapterService.CheckIpv6Supported(adapter);
                if (reason != null) SetStatus(reason);
            }
        }

        /// <summary>
        /// IPv6 页的地址区：“手动设置”时是输入框；“自动获取”时是状态列表，
        /// 因为自动获取的地址常有好几个（公网、临时、本地链接……），而且完整地址放不进输入框
        /// </summary>
        private void UpdateIpv6View()
        {
            bool v6 = IsIpv6Page, manual = IsManualIpv6;
            lblIpAddress.Visible = lblMask.Visible = lblGateway.Visible = !v6 || manual;
            txt6Address.Visible = txt6Prefix.Visible = txt6Gateway.Visible = v6 && manual;
            bool status = v6 && !manual;
            for (int i = 0; i < Ipv6StatusRows; i++)
            {
                bool row = status && i < ipv6Status.Rows.Count;
                lbl6Status[i].Visible = row;
                txt6Status[i].Visible = row;
            }
            lbl6Notice.Visible = status && ipv6Status.Notice != null;
            PerformLayout();
        }

        /// <summary>
        /// 自动获取时的状态列表
        /// </summary>
        private void ShowIpv6Status(NetworkAdapter adapter)
        {
            ipv6Status = Ipv6Status.Describe(adapter, Ipv6StatusRows);
            for (int i = 0; i < Ipv6StatusRows; i++)
            {
                Ipv6StatusRow row = i < ipv6Status.Rows.Count ? ipv6Status.Rows[i] : null;
                lbl6Status[i].Text = row == null ? "" : row.Caption;
                txt6Status[i].Text = row == null ? "" : row.Value;
                InkToolTip.Shared.SetToolTip(txt6Status[i], row == null ? "" : row.Detail);
                InkToolTip.Shared.SetToolTip(lbl6Status[i], row == null ? "" : row.Detail);
            }
            lbl6Notice.Text = ipv6Status.Notice ?? "";
            UpdateIpv6View();
        }

        /// <summary>
        /// 左侧网卡卡片的 IPv6：每个地址前标出类别，太长时中间省略，悬停可看完整地址
        /// </summary>
        private void ShowIpv6List(NetworkAdapter adapter)
        {
            List<Ipv6AddressInfo> list = adapter == null ? new List<Ipv6AddressInfo>() : Ipv6Status.ForDisplay(adapter.Ipv6LiveAddresses, out _);
            if (list.Count == 0)
            {
                txtIPv6.Text = "-";
                InkToolTip.Shared.SetToolTip(txtIPv6, "");
                return;
            }
            int width = txtIPv6.ClientSize.Width - P(6);
            txtIPv6.Text = string.Join(Environment.NewLine, list.Select(a => FitIpv6Line(Ipv6Status.ShortNameOf(a) + "  ", a.Address, width)));
            InkToolTip.Shared.SetToolTip(txtIPv6, string.Join("\n", list.Select(a => $"{Ipv6Status.NameOf(a)}  {a.Address}/{a.PrefixLength}")));
        }

        /// <summary>
        /// 放不下时把地址中间换成省略号，如 2408:8207:…:a627:f521
        /// </summary>
        private string FitIpv6Line(string label, string address, int width)
        {
            Font font = txtIPv6.Font;
            Func<string, int> measure = text => TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding).Width;
            if (width <= 0 || measure(label + address) <= width) return label + address;
            for (int keep = address.Length - 1; keep >= 8; keep--)
            {
                int head = (keep + 1) / 2;
                string text = label + address.Substring(0, head) + "…" + address.Substring(address.Length - (keep - head));
                if (measure(text) <= width) return text;
            }
            return label + address;
        }

        /// <summary>
        /// 在 ShowAdapter 中调用（isLoadingFields 期间）
        /// </summary>
        private void ShowIpv6(NetworkAdapter adapter)
        {
            ShowIpv6List(adapter);
            ShowIpv6Status(adapter);
            bool has = adapter != null;
            txt6Address.Text = has ? adapter.Ipv6Address : "";
            txt6Prefix.Text = has ? adapter.Ipv6PrefixLength : "";
            txt6Gateway.Text = has ? adapter.Ipv6Gateway : "";
            txt6DnsMain.Text = has ? adapter.Ipv6DnsMain : "";
            txt6DnsBackup.Text = has ? adapter.Ipv6DnsBackup : "";
            seg6Mode.SelectedIndex = has && adapter.HasManualIpv6 ? 1 : 0;
            seg6DnsMode.SelectedIndex = has && adapter.HasStaticIpv6Dns ? 1 : 0;
        }

        private void UpdateIpv6States(bool idle, NetworkAdapter adapter)
        {
            bool editable = idle && adapter != null && AdapterService.CheckIpv6Supported(adapter) == null;
            bool manual = IsManualIpv6;
            seg6Mode.Enabled = editable;
            txt6Address.Enabled = txt6Prefix.Enabled = txt6Gateway.Enabled = editable && manual;
            seg6DnsMode.Enabled = editable;
            txt6DnsMain.Enabled = txt6DnsBackup.Enabled = editable && IsManualIpv6Dns;
            if (IsIpv6Page) btnDnsPreset.Enabled = editable;
        }

        private void Ipv6Mode_Changed(object sender, EventArgs e)
        {
            UpdateIpv6View();
            if (isLoadingFields) return;
            if (IsManualIpv6 && txt6Prefix.Text.Trim().Length == 0)
            {
                // 路由器分配的网段几乎都是 /64；网关沿用自动获取到的
                isLoadingFields = true;
                txt6Prefix.Text = Ipv6Validator.DefaultPrefixLength.ToString();
                isLoadingFields = false;
            }
            OnIpv6ModeChanged();
            if (IsManualIpv6)
            {
                txt6Address.FocusInput();
                SetStatus("手动地址会与自动获取的地址同时生效。按 Enter 应用，Esc 还原");
            }
        }

        private void OnIpv6ModeChanged()
        {
            if (isLoadingFields) return;
            foreach (FieldBox box in Ipv6Fields) box.SetError(null, false);
            MarkDirty(true);
        }

        /// <summary>
        /// 把“2001:db8::10/64”拆成地址和前缀长度；只填了地址时前缀长度先给 64
        /// </summary>
        private void NormalizeIpv6CidrInput()
        {
            if (!txt6Address.Enabled) return;
            IPAddress address;
            int prefix;
            if (Ipv6Validator.TryParseCidr(txt6Address.Text, out address, out prefix))
            {
                txt6Address.Text = address.ToString();
                txt6Prefix.Text = prefix.ToString();
            }
            else if (txt6Prefix.Text.Trim().Length == 0 && Ipv6Validator.TryParse(txt6Address.Text, out address))
            {
                txt6Prefix.Text = Ipv6Validator.DefaultPrefixLength.ToString();
            }
        }

        /// <summary>
        /// 离开输入框时只检查格式，网关是否在网段内等在应用时统一检查
        /// </summary>
        private void ValidateIpv6Field(FieldBox box)
        {
            if (!box.Enabled) return;
            string text = box.Text.Trim();
            string error = "";
            IPAddress address;
            int prefix;
            if (text.Length > 0)
            {
                if (box == txt6Prefix)
                {
                    if (!int.TryParse(text, out prefix) || prefix < 1 || prefix > 128) error = "前缀长度应为 1–128 的整数，通常为 64";
                }
                else if (!Ipv6Validator.TryParse(text, out address))
                {
                    error = "格式不正确，应为 IPv6 地址，如 2001:db8::10";
                }
            }
            if (error != box.ErrorText) box.SetError(error);
        }

        private Ipv6ConfigRequest BuildIpv6Request()
        {
            NormalizeIpv6CidrInput();
            return new Ipv6ConfigRequest
            {
                UseAuto = !IsManualIpv6,
                Address = txt6Address.Text.Trim(),
                PrefixLength = txt6Prefix.Text.Trim(),
                Gateway = txt6Gateway.Text.Trim(),
                UseAutoDns = !IsManualIpv6Dns,
                DnsServers = IsManualIpv6Dns ? Ipv6Validator.DnsList(txt6DnsMain.Text, txt6DnsBackup.Text) : new string[0]
            };
        }

        /// <summary>
        /// 把一份 IPv6 配置填入表单，作为尚未应用的修改（以管理员身份重启后回填）
        /// </summary>
        private void ShowIpv6Request(Ipv6ConfigRequest request)
        {
            isLoadingFields = true;
            try
            {
                seg6Mode.SelectedIndex = request.UseAuto ? 0 : 1;
                seg6DnsMode.SelectedIndex = request.UseAutoDns ? 0 : 1;
                if (!request.UseAuto)
                {
                    txt6Address.Text = request.Address;
                    txt6Prefix.Text = request.PrefixLength;
                    txt6Gateway.Text = request.Gateway;
                }
                if (!request.UseAutoDns)
                {
                    txt6DnsMain.Text = request.DnsServers.Length > 0 ? request.DnsServers[0] : "";
                    txt6DnsBackup.Text = request.DnsServers.Length > 1 ? request.DnsServers[1] : "";
                }
            }
            finally
            {
                isLoadingFields = false;
            }
            MarkDirty(true);
        }

        private void ShowIpv6ValidationErrors(List<ValidationError> errors)
        {
            foreach (FieldBox box in Ipv6Fields) box.SetError(null, false);
            foreach (ValidationError error in errors)
            {
                FieldBox box = Ipv6FieldOf(error.Field);
                if (box.ErrorText.Length == 0) box.SetError(error.Message);
            }
        }

        private FieldBox Ipv6FieldOf(IpField field)
        {
            switch (field)
            {
                case IpField.IpAddress: return txt6Address;
                case IpField.SubnetMask: return txt6Prefix;
                case IpField.Gateway: return txt6Gateway;
                case IpField.DnsMain: return txt6DnsMain;
                default: return txt6DnsBackup;
            }
        }

        private void ShowIpv6DnsPresetMenu()
        {
            Menus.Clear(dns6PresetMenu);
            foreach (KeyValuePair<string, string[]> preset in ProfileManager.PresetIpv6Dns)
            {
                string[] servers = preset.Value;
                dns6PresetMenu.Items.Add(Menus.Item(preset.Key, string.Join(" / ", servers), Glyph.Server, (s, ev) => ApplyIpv6DnsPreset(servers)));
            }
            dns6PresetMenu.Show(btnDnsPreset, new System.Drawing.Point(0, btnDnsPreset.Height + P(4)));
        }

        private void ApplyIpv6DnsPreset(string[] servers)
        {
            if (!IsManualIpv6Dns) seg6DnsMode.SelectedIndex = 1;
            txt6DnsMain.Text = servers.Length > 0 ? servers[0] : "";
            txt6DnsBackup.Text = servers.Length > 1 ? servers[1] : "";
        }

        /// <summary>
        /// 切到 IPv6 页（界面截图测试用）
        /// </summary>
        internal void ShowIpv6Page()
        {
            segFamily.SelectedIndex = 1;
        }
    }
}
