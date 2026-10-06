using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using IP_UpdateTest.Core;
using IP_UpdateTest.Ui;

namespace IP_UpdateTest
{
    /// <summary>
    /// 新建或编辑配置方案：在配置方案窗口内展开的面板，保存成功后返回保存的方案
    /// </summary>
    public sealed class ProfileEditSheet : SheetContent
    {
        private const float PanelWidth = 520;
        private const float LabelColumn = 84;
        private const float Step = 44;
        private const float FirstRow = 60;

        private readonly ThemedForm owner;
        private readonly FieldBox txtName = new FieldBox { Glyph = Glyph.Tag };
        private readonly Segmented segIpMode = new Segmented();
        private readonly FieldBox txtIp = new FieldBox { Glyph = Glyph.Monitor, Mono = true };
        private readonly FieldBox txtMask = new FieldBox { Glyph = Glyph.Hash, Mono = true };
        private readonly FieldBox txtGateway = new FieldBox { Glyph = Glyph.Router, Mono = true };
        private readonly Segmented segDnsMode = new Segmented();
        private readonly FieldBox txtDnsMain = new FieldBox { Glyph = Glyph.Server, Mono = true };
        private readonly FieldBox txtDnsBackup = new FieldBox { Glyph = Glyph.Server, Mono = true };
        private readonly SelectBox cbxAdapter = new SelectBox { Glyph = Glyph.Network };
        private readonly SpringButton btnSave = FooterButton("保存", ButtonKind.Primary);
        private readonly SpringButton btnCancel = FooterButton("取消", ButtonKind.Secondary);
        private readonly string[] captions = { "名称", "IP 获取", "IP 地址", "子网掩码", "默认网关", "DNS 获取", "首选 DNS", "备用 DNS", "绑定网卡" };

        /// <summary>
        /// 编辑前的名称；新建时为 null
        /// </summary>
        private readonly string originalName;
        private readonly DateTime createTime;
        private bool confirmOverwrite;

        public ProfileEditSheet(ThemedForm owner, IpProfile profile, IEnumerable<NetworkAdapter> adapters)
        {
            this.owner = owner;
            Title = profile == null ? "新建配置方案" : "编辑配置方案";
            originalName = profile == null ? null : profile.Name;
            createTime = profile == null ? DateTime.Now : profile.CreateTime;

            segIpMode.SetItems("自动获取 (DHCP)", "手动设置");
            segDnsMode.SetItems("自动获取", "手动设置");
            txtName.Placeholder = "如：公司、家里";
            txtDnsBackup.Placeholder = "可不填";
            cbxAdapter.Format = o => ((AdapterChoice)o).Text;
            cbxAdapter.Detail = o => ((AdapterChoice)o).Mac;
            cbxAdapter.ItemGlyph = o => ((AdapterChoice)o).Mac == null ? Glyph.Minus : Glyph.Network;

            Controls.AddRange(new Control[] { txtName, segIpMode, txtIp, txtMask, txtGateway, segDnsMode, txtDnsMain, txtDnsBackup, cbxAdapter, btnSave, btnCancel });
            int tab = 0;
            foreach (Control c in new Control[] { txtName, segIpMode, txtIp, txtMask, txtGateway, segDnsMode, txtDnsMain, txtDnsBackup, cbxAdapter, btnSave, btnCancel })
                c.TabIndex = tab++;
            foreach (FieldBox field in Fields)
            {
                RegisterNative(field.Box);
                FieldBox f = field;
                f.TextChanged += (s, e) => f.SetError(null);
            }

            txtName.TextChanged += (s, e) =>
            {
                if (!confirmOverwrite) return;
                confirmOverwrite = false;
                btnSave.Text = "保存";
                PerformLayout();
            };
            LoadAdapters(adapters, profile == null ? null : profile.AdapterMac);
            LoadProfile(profile);
            segIpMode.SelectedIndexChanged += (s, e) => UpdateStates();
            segDnsMode.SelectedIndexChanged += (s, e) => UpdateStates();
            UpdateStates();

            btnSave.Click += (s, e) => Save();
            btnCancel.Click += (s, e) => Cancel();
            Accept = btnSave;
        }

        private IEnumerable<FieldBox> Fields
        {
            get { return new[] { txtName, txtIp, txtMask, txtGateway, txtDnsMain, txtDnsBackup }; }
        }

        public override Control InitialFocus
        {
            get { return txtName; }
        }

        public override Size LogicalSize
        {
            get { return new Size((int)PanelWidth, (int)(FirstRow + captions.Length * Step + 22 + 20 + Metrics.PanelButtonHeight + 20)); }
        }

        private void LoadAdapters(IEnumerable<NetworkAdapter> adapters, string boundMac)
        {
            var choices = new List<AdapterChoice> { new AdapterChoice(null, "不绑定") };
            foreach (NetworkAdapter adapter in adapters.Where(a => a.MacAddressText.Length > 0))
                choices.Add(new AdapterChoice(adapter.MacAddressText, adapter.Name));

            // 绑定的网卡不在列表中（如已拔出）时也保留，避免保存时丢掉绑定
            if (!string.IsNullOrEmpty(boundMac) && !choices.Any(c => c.Mac == boundMac))
                choices.Add(new AdapterChoice(boundMac, "未连接的网卡"));

            int index = Math.Max(0, choices.FindIndex(c => c.Mac == boundMac));
            cbxAdapter.SetItems(choices.Cast<object>(), index);
        }

        private void LoadProfile(IpProfile profile)
        {
            if (profile == null)
            {
                segIpMode.SelectedIndex = 0;
                segDnsMode.SelectedIndex = 0;
                return;
            }

            txtName.Text = profile.Name;
            IpConfigRequest request = profile.ToRequest();
            segIpMode.SelectedIndex = request.UseDhcp ? 0 : 1;
            txtIp.Text = profile.IpAddress;
            txtMask.Text = profile.SubnetMask;
            txtGateway.Text = profile.Gateway;
            segDnsMode.SelectedIndex = request.UseDhcp && request.UseDhcpDns ? 0 : 1;
            txtDnsMain.Text = request.DnsServers.Length > 0 ? request.DnsServers[0] : "";
            txtDnsBackup.Text = request.DnsServers.Length > 1 ? request.DnsServers[1] : "";
        }

        private bool IsStatic
        {
            get { return segIpMode.SelectedIndex == 1; }
        }

        private bool IsManualDns
        {
            get { return segDnsMode.SelectedIndex == 1; }
        }

        private void UpdateStates()
        {
            bool isStatic = IsStatic;
            if (isStatic && !IsManualDns) segDnsMode.SelectedIndex = 1; // 静态 IP 时 DNS 只能手动设置
            segDnsMode.SetItemEnabled(0, !isStatic);
            txtIp.Enabled = txtMask.Enabled = txtGateway.Enabled = isStatic;
            txtDnsMain.Enabled = txtDnsBackup.Enabled = IsManualDns;
        }

        private IpProfile BuildProfile()
        {
            bool isDhcp = !IsStatic;
            bool manualDns = IsManualDns;
            var adapter = cbxAdapter.SelectedItem as AdapterChoice;
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
                AdapterMac = adapter == null ? null : adapter.Mac,
                CreateTime = createTime
            };
        }

        private void Save()
        {
            foreach (FieldBox field in Fields) field.SetError(null, false);
            IpProfile profile = BuildProfile();
            if (profile.Name.Length == 0)
            {
                txtName.SetError("请填写名称");
                txtName.FocusInput();
                return;
            }

            List<ValidationError> errors = profile.ToRequest().Validate();
            foreach (ValidationError error in errors)
            {
                FieldBox field = FieldOf(error.Field);
                if (field.ErrorText.Length == 0) field.SetError(error.Message);
            }
            if (errors.Count > 0)
            {
                FieldOf(errors[0].Field).FocusInput();
                return;
            }

            // 新建时与已有方案重名：在名称框下给出提示，再点一次保存才覆盖
            if (originalName == null && ProfileManager.Find(profile.Name) != null && !confirmOverwrite)
            {
                confirmOverwrite = true;
                btnSave.Text = "覆盖同名方案";
                txtName.SetError($"已存在名为“{profile.Name}”的方案，再点一次将覆盖它");
                PerformLayout();
                return;
            }

            try
            {
                if (originalName == null) ProfileManager.Add(profile);
                else ProfileManager.Update(originalName, profile);
            }
            catch (ArgumentException ex)
            {
                txtName.SetError(ex.Message);
                return;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                btnSave.Fail("保存失败");
                owner.Notify(NoticeKind.Error, "保存失败：" + ex.Message);
                return;
            }

            Close(profile);
        }

        private FieldBox FieldOf(IpField field)
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

        protected override void LayoutContent(float s)
        {
            int x = P(24 + LabelColumn);
            int width = Width - x - P(24);
            int h = P(Metrics.InputHeight);
            Control[] rows = { txtName, segIpMode, txtIp, txtMask, txtGateway, segDnsMode, txtDnsMain, txtDnsBackup, cbxAdapter };
            for (int i = 0; i < rows.Length; i++)
            {
                int y = P(FirstRow + i * Step);
                var visual = new Rectangle(x, y, width, h);
                var field = rows[i] as FieldBox;
                var select = rows[i] as SelectBox;
                var seg = rows[i] as Segmented;
                if (field != null) field.Place(visual);
                else if (select != null) select.Place(visual);
                else if (seg != null)
                {
                    int sh = P(Metrics.ButtonHeight);
                    seg.SetBounds(x, y + (h - sh) / 2, Math.Min(width, seg.PreferredWidth), sh);
                }
            }
            LayoutFooter(s, btnSave, btnCancel);
        }

        protected override void PaintContent(Graphics g, double opacity, double blur)
        {
            base.PaintContent(g, opacity, blur);
            float s = S;
            for (int i = 0; i < captions.Length; i++)
            {
                var row = new RectangleF(P(24), P(FirstRow + i * Step), P(LabelColumn), P(Metrics.InputHeight));
                Typo.DrawLine(g, captions[i], TextStyle.Body, Palette.Ink2, row.X, Typo.CenterBaseline(row, TextStyle.Body, s), s, opacity, blur);
            }
            var hint = new RectangleF(P(24 + LabelColumn + 2), P(FirstRow + captions.Length * Step), Width - P(48 + LabelColumn), P(18));
            Typo.DrawLine(g, "绑定后总是应用到该网卡；不绑定则应用到当前选中的网卡", TextStyle.Small, Palette.Ink3, hint.X,
                Typo.CenterBaseline(hint, TextStyle.Small, s), s, opacity, blur);
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
        }
    }
}
