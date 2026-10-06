using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Windows.Forms;
using IP_UpdateTest.Core;
using IP_UpdateTest.Ui;

namespace IP_UpdateTest
{
    /// <summary>
    /// 配置方案管理：新建、编辑、复制、删除、排序、导入导出。编辑在窗口内的面板里完成
    /// </summary>
    public class FrmProfiles : ThemedForm
    {
        private readonly List<NetworkAdapter> adapters;
        private readonly DataTable table = new DataTable
        {
            EmptyGlyph = Glyph.Layers,
            EmptyTitle = "还没有配置方案",
            EmptyText = "把常用的 IP / DNS 存成方案，之后一键切换"
        };

        private readonly SpringButton btnNew = new SpringButton { Kind = ButtonKind.Secondary, Glyph = Glyph.Plus, Text = "新建" };
        private readonly SpringButton btnEdit = IconButton(Glyph.Pencil, "编辑（Enter）");
        private readonly SpringButton btnCopy = IconButton(Glyph.Copy, "复制一份");
        private readonly SpringButton btnUp = IconButton(Glyph.ArrowUp, "上移");
        private readonly SpringButton btnDown = IconButton(Glyph.ArrowDown, "下移");
        private readonly SpringButton btnDelete = IconButton(Glyph.Trash, "删除（Delete）");
        private readonly SpringButton btnImport = IconButton(Glyph.Download, "从文件导入");
        private readonly SpringButton btnExport = IconButton(Glyph.Upload, "导出到文件");
        private readonly SpringButton btnClose = new SpringButton { Kind = ButtonKind.Secondary, Text = "关闭" };
        private readonly SpringButton btnApply = new SpringButton { Kind = ButtonKind.Primary, Text = "应用到网卡" };
        private readonly ContextMenuStrip rowMenu = Menus.Create();

        /// <summary>
        /// 用户选择“应用到网卡”的方案
        /// </summary>
        public IpProfile ProfileToApply { get; private set; }

        public FrmProfiles(List<NetworkAdapter> adapters)
        {
            this.adapters = adapters ?? new List<NetworkAdapter>();
            Text = "配置方案";
            TitleBar.ShowMinimize = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            SetLogicalSize(720, 600);

            table.Columns.Add(new TableColumn("名称", 150, CellKind.Strong));
            table.Columns.Add(new TableColumn("配置", 0, CellKind.Mono));
            table.Columns.Add(new TableColumn("绑定网卡", 150, CellKind.Chip));
            table.SelectionChanged += (s, e) => UpdateButtons();
            table.RowActivated += async (s, e) => await EditSelectedAsync(null);
            table.DeletePressed += async (s, e) => await DeleteSelectedAsync(null);
            table.RowMenuRequested += (s, e) => ShowRowMenu(e.Location);

            btnNew.Click += async (s, e) => await CreateProfileAsync();
            btnEdit.Click += async (s, e) => await EditSelectedAsync(btnEdit);
            btnCopy.Click += (s, e) => CopySelected();
            btnUp.Click += (s, e) => MoveSelected(-1);
            btnDown.Click += (s, e) => MoveSelected(1);
            btnDelete.Click += async (s, e) => await DeleteSelectedAsync(btnDelete);
            btnImport.Click += async (s, e) => await ImportProfilesAsync();
            btnExport.Click += (s, e) => ExportProfiles();
            btnClose.Click += (s, e) => Close();
            btnApply.Click += (s, e) => ApplySelected();

            Controls.AddRange(new Control[] { btnNew, btnEdit, btnCopy, btnUp, btnDown, btnDelete, btnImport, btnExport, table, btnClose, btnApply });
            int tab = 0;
            foreach (Control c in new Control[] { table, btnNew, btnEdit, btnCopy, btnUp, btnDown, btnDelete, btnImport, btnExport, btnClose, btnApply })
                c.TabIndex = tab++;
            CancelButton = btnClose;

            Reload(null, true);
        }

        private static SpringButton IconButton(Glyph glyph, string tip)
        {
            var button = new SpringButton { Kind = ButtonKind.Ghost, Glyph = glyph };
            InkToolTip.Shared.SetToolTip(button, tip);
            return button;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            table.Focus();
        }

        protected override void LayoutContent(float s)
        {
            int gutter = P(Metrics.PageGutter);
            int top = P(Metrics.TitleBarHeight + 4);
            int h = P(Metrics.ButtonHeight);

            btnNew.SetBounds(gutter, top, Math.Max(P(76), btnNew.PreferredWidth), h);
            int x = btnNew.Right + P(10);
            foreach (SpringButton b in new[] { btnEdit, btnCopy, btnUp, btnDown, btnDelete })
            {
                b.SetBounds(x, top, h, h);
                x += h + P(2);
            }
            x = ClientSize.Width - gutter - h;
            foreach (SpringButton b in new[] { btnExport, btnImport })
            {
                b.SetBounds(x, top, h, h);
                x -= h + P(2);
            }

            int footerH = P(Metrics.PrimaryHeight);
            int footerY = ClientSize.Height - P(20) - footerH;
            int tableTop = top + h + P(12);
            table.SetBounds(gutter, tableTop, ClientSize.Width - gutter * 2, footerY - P(16) - tableTop);

            int applyW = Math.Max(P(128), btnApply.PreferredWidth);
            btnApply.SetBounds(ClientSize.Width - gutter - applyW, footerY, applyW, footerH);
            int closeW = Math.Max(P(84), btnClose.PreferredWidth);
            btnClose.SetBounds(btnApply.Left - P(8) - closeW, footerY + (footerH - h) / 2, closeW, h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            float s = S;
            string text = ProfileManager.Profiles.Count == 0 ? "" : $"共 {ProfileManager.Profiles.Count} 个方案 · 双击编辑，右键查看更多操作";
            var area = new RectangleF(P(Metrics.PageGutter), btnClose.Top, btnClose.Left - P(Metrics.PageGutter) - P(12), btnClose.Height);
            Shapes.Prepare(e.Graphics);
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Typo.Draw(e.Graphics, text, TextStyle.Small, Palette.Ink3, area, s);
        }

        private IpProfile SelectedProfile
        {
            get { return table.SelectedRow == null ? null : (IpProfile)table.SelectedRow.Tag; }
        }

        /// <summary>
        /// 重新填充列表，并选中指定名称的方案
        /// </summary>
        private void Reload(string selectName, bool stagger)
        {
            List<IpProfile> profiles = ProfileManager.Profiles.ToList();
            int select = profiles.FindIndex(p => ProfileManager.SameName(p.Name, selectName));
            if (select < 0) select = Math.Min(Math.Max(0, table.SelectedIndex), profiles.Count - 1);
            table.SetRows(profiles.Select(p => new TableRow(p, p.Name, p.Summary, BoundAdapterText(p))), select, stagger);
            UpdateButtons();
            Invalidate();
        }

        private string BoundAdapterText(IpProfile profile)
        {
            if (string.IsNullOrEmpty(profile.AdapterMac)) return "当前网卡";
            NetworkAdapter adapter = adapters.FirstOrDefault(a => a.MacAddressText == profile.AdapterMac);
            return adapter != null ? adapter.Name : profile.AdapterMac;
        }

        private void UpdateButtons()
        {
            int index = table.SelectedIndex;
            bool has = index >= 0;
            btnEdit.Enabled = btnCopy.Enabled = btnDelete.Enabled = btnApply.Enabled = has;
            btnUp.Enabled = index > 0;
            btnDown.Enabled = has && index < table.Rows.Count - 1;
            btnExport.Enabled = table.Rows.Count > 0;
        }

        private void ShowRowMenu(Point location)
        {
            IpProfile profile = SelectedProfile;
            if (profile == null) return;
            int index = table.SelectedIndex;
            Menus.Clear(rowMenu);
            rowMenu.Items.Add(Menus.Item("应用到网卡", Glyph.Check, (s, e) => ApplySelected()));
            rowMenu.Items.Add(Menus.Item("编辑…", Glyph.Pencil, async (s, e) => await EditSelectedAsync(null)));
            rowMenu.Items.Add(Menus.Item("复制一份", Glyph.Copy, (s, e) => CopySelected()));
            rowMenu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem up = Menus.Item("上移", Glyph.ArrowUp, (s, e) => MoveSelected(-1));
            up.Enabled = index > 0;
            ToolStripMenuItem down = Menus.Item("下移", Glyph.ArrowDown, (s, e) => MoveSelected(1));
            down.Enabled = index < table.Rows.Count - 1;
            rowMenu.Items.Add(up);
            rowMenu.Items.Add(down);
            rowMenu.Items.Add(new ToolStripSeparator());
            rowMenu.Items.Add(Menus.Item("删除", Glyph.Trash, async (s, e) => await DeleteSelectedAsync(null), true));
            rowMenu.Show(table, location);
        }

        /// <summary>
        /// 打开编辑面板。origin 为空时从所选行展开
        /// </summary>
        private async Task OpenEditorAsync(IpProfile profile, Control origin)
        {
            var sheet = new ProfileEditSheet(this, profile, adapters);
            Task<object> task;
            if (origin != null) task = SheetOverlay.Show(this, sheet, origin);
            else
            {
                Rectangle row = table.RowBounds(table.SelectedIndex);
                Rectangle rect = RectangleToClient(table.RectangleToScreen(row));
                task = SheetOverlay.Show(this, sheet, rect, Palette.AccentSoft, 0);
            }
            var saved = await task as IpProfile;
            if (saved == null || IsDisposed) return;
            Reload(saved.Name, false);
            Notify(NoticeKind.Success, (profile == null ? "已新建" : "已保存") + "“" + saved.Name + "”");
        }

        private Task CreateProfileAsync()
        {
            return OpenEditorAsync(null, btnNew);
        }

        private Task EditSelectedAsync(Control origin)
        {
            IpProfile profile = SelectedProfile;
            if (profile == null || HasSheet) return Task.FromResult(0);
            return OpenEditorAsync(profile, origin);
        }

        private void CopySelected()
        {
            IpProfile profile = SelectedProfile;
            if (profile == null) return;

            IpProfile copy = profile.Clone();
            int n = 2;
            copy.Name = profile.Name + " - 副本";
            while (ProfileManager.Find(copy.Name) != null) copy.Name = $"{profile.Name} - 副本 {n++}";
            if (Run(() => ProfileManager.Add(copy), copy.Name)) Notify(NoticeKind.Success, "已复制为“" + copy.Name + "”");
        }

        private async Task DeleteSelectedAsync(Control origin)
        {
            IpProfile profile = SelectedProfile;
            if (profile == null || HasSheet) return;
            if (!await ConfirmAsync(origin, "删除配置方案", $"确定删除“{profile.Name}”？删除后不能恢复。", "删除", true)) return;
            if (Run(() => ProfileManager.Remove(profile.Name), null)) Notify(NoticeKind.Success, "已删除“" + profile.Name + "”");
        }

        private void MoveSelected(int offset)
        {
            IpProfile profile = SelectedProfile;
            if (profile != null) Run(() => ProfileManager.Move(profile.Name, offset), profile.Name);
        }

        private void ApplySelected()
        {
            ProfileToApply = SelectedProfile;
            if (ProfileToApply == null) return;
            DialogResult = DialogResult.OK;
        }

        /// <summary>
        /// 执行修改并刷新列表；保存失败时提示
        /// </summary>
        private bool Run(Action action, string selectName)
        {
            bool ok = true;
            try
            {
                action();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                ok = false;
                Notify(NoticeKind.Error, "保存失败：" + ex.Message);
            }
            Reload(selectName, false);
            return ok;
        }

        private async Task ImportProfilesAsync()
        {
            string fileName;
            using (var dialog = new OpenFileDialog { Filter = "JSON 文件|*.json" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                fileName = dialog.FileName;
            }

            try
            {
                List<IpProfile> imported = ProfileManager.ReadFile(fileName);
                int conflicts = imported.Count(p => ProfileManager.Find(p.Name) != null);
                bool overwrite = false;
                if (conflicts > 0)
                {
                    int answer = await Sheets.ChooseAsync(this, btnImport, "导入配置方案",
                        $"文件里有 {conflicts} 个方案与现有方案同名，要怎么处理？", "覆盖同名方案", "跳过同名方案");
                    if (answer < 0) return;
                    overwrite = answer == 0;
                }

                ImportResult result = ProfileManager.Import(imported, overwrite);
                Reload(null, true);
                Notify(NoticeKind.Success, $"导入完成：新增 {result.Added}，覆盖 {result.Replaced}，跳过 {result.Skipped}");
            }
            catch (SerializationException)
            {
                Notify(NoticeKind.Warning, "文件格式不正确，不是本工具导出的配置方案");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Notify(NoticeKind.Error, "导入失败：" + ex.Message);
            }
        }

        private void ExportProfiles()
        {
            using (var dialog = new SaveFileDialog { Filter = "JSON 文件|*.json", FileName = "ip_profiles.json" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    ProfileManager.Export(dialog.FileName);
                    Notify(NoticeKind.Success, $"已导出 {ProfileManager.Profiles.Count} 个配置方案");
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Notify(NoticeKind.Error, "导出失败：" + ex.Message);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) rowMenu.Dispose();
            base.Dispose(disposing);
        }
    }
}
