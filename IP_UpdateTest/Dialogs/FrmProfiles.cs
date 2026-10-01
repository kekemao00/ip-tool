using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Windows.Forms;
using IP_UpdateTest.Core;

namespace IP_UpdateTest
{
    /// <summary>
    /// 配置方案管理：新建、编辑、复制、删除、排序、导入导出
    /// </summary>
    public class FrmProfiles : DialogBase
    {
        private readonly ListView list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false
        };

        private readonly List<NetworkAdapter> adapters;
        private readonly Button btnEdit;
        private readonly Button btnCopy;
        private readonly Button btnDelete;
        private readonly Button btnUp;
        private readonly Button btnDown;
        private readonly Button btnApply;

        /// <summary>
        /// 用户选择“应用到网卡”的方案
        /// </summary>
        public IpProfile ProfileToApply { get; private set; }

        public FrmProfiles(List<NetworkAdapter> adapters) : base("配置方案", 640, 420)
        {
            this.adapters = adapters ?? new List<NetworkAdapter>();

            list.Columns.Add("名称", 120);
            list.Columns.Add("配置", 272);
            list.Columns.Add("绑定网卡", 100);
            list.DoubleClick += (s, e) => EditSelected();
            list.SelectedIndexChanged += (s, e) => UpdateButtons();
            list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete) DeleteSelected();
            };

            var side = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                Width = 100,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(12, 0, 0, 0)
            };
            side.Controls.Add(SideButton("新建…", (s, e) => CreateProfile()));
            btnEdit = SideButton("编辑…", (s, e) => EditSelected());
            btnCopy = SideButton("复制", (s, e) => CopySelected());
            btnDelete = SideButton("删除", (s, e) => DeleteSelected());
            btnUp = SideButton("上移", (s, e) => MoveSelected(-1));
            btnDown = SideButton("下移", (s, e) => MoveSelected(1));
            side.Controls.AddRange(new Control[] { btnEdit, btnCopy, btnDelete, btnUp, btnDown });
            Button btnImport = SideButton("导入…", (s, e) => ImportProfiles());
            btnImport.Margin = new Padding(0, 18, 0, 6);
            side.Controls.Add(btnImport);
            side.Controls.Add(SideButton("导出…", (s, e) => ExportProfiles()));

            ContentPanel.Controls.Add(list);
            ContentPanel.Controls.Add(side);

            CancelButton = AddButton("关闭", false, (s, e) => DialogResult = DialogResult.Cancel);
            btnApply = AddButton("应用到网卡", true, (s, e) => ApplySelected(), 100);
            FinishLayout();

            list.Font = UITheme.InputFont;
            list.ForeColor = UITheme.TextPrimary;
            Reload(null);
        }

        private Button SideButton(string text, EventHandler onClick)
        {
            var button = new Button { Text = text, Size = new Size(88, 30), Margin = new Padding(0, 0, 0, 6) };
            UITheme.StyleSecondaryButton(button);
            button.Click += onClick;
            return button;
        }

        private IpProfile SelectedProfile
        {
            get { return list.SelectedItems.Count > 0 ? (IpProfile)list.SelectedItems[0].Tag : null; }
        }

        /// <summary>
        /// 重新填充列表，并选中指定名称的方案
        /// </summary>
        private void Reload(string selectName)
        {
            list.BeginUpdate();
            list.Items.Clear();
            foreach (IpProfile profile in ProfileManager.Profiles)
            {
                var item = new ListViewItem(new[] { profile.Name, profile.Summary, BoundAdapterText(profile) }) { Tag = profile };
                list.Items.Add(item);
                if (ProfileManager.SameName(profile.Name, selectName)) item.Selected = true;
            }
            if (list.SelectedItems.Count == 0 && list.Items.Count > 0) list.Items[0].Selected = true;
            list.EndUpdate();
            UpdateButtons();
        }

        private string BoundAdapterText(IpProfile profile)
        {
            if (string.IsNullOrEmpty(profile.AdapterMac)) return "当前网卡";
            NetworkAdapter adapter = adapters.FirstOrDefault(a => a.MacAddressText == profile.AdapterMac);
            return adapter != null ? adapter.Name : profile.AdapterMac;
        }

        private void UpdateButtons()
        {
            IpProfile selected = SelectedProfile;
            int index = list.SelectedIndices.Count > 0 ? list.SelectedIndices[0] : -1;
            btnEdit.Enabled = btnCopy.Enabled = btnDelete.Enabled = btnApply.Enabled = selected != null;
            btnUp.Enabled = index > 0;
            btnDown.Enabled = index >= 0 && index < list.Items.Count - 1;
        }

        private void CreateProfile()
        {
            using (var dialog = new FrmProfileEdit(null, adapters))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) Reload(dialog.Profile.Name);
            }
        }

        private void EditSelected()
        {
            IpProfile profile = SelectedProfile;
            if (profile == null) return;
            using (var dialog = new FrmProfileEdit(profile, adapters))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) Reload(dialog.Profile.Name);
            }
        }

        private void CopySelected()
        {
            IpProfile profile = SelectedProfile;
            if (profile == null) return;

            IpProfile copy = profile.Clone();
            int n = 2;
            copy.Name = profile.Name + " - 副本";
            while (ProfileManager.Find(copy.Name) != null) copy.Name = $"{profile.Name} - 副本 {n++}";
            Run(() => ProfileManager.Add(copy), copy.Name);
        }

        private void DeleteSelected()
        {
            IpProfile profile = SelectedProfile;
            if (profile == null || !UITheme.Confirm($"确定删除配置方案“{profile.Name}”？", "删除配置方案")) return;
            Run(() => ProfileManager.Remove(profile.Name), null);
        }

        private void MoveSelected(int offset)
        {
            IpProfile profile = SelectedProfile;
            if (profile != null) Run(() => ProfileManager.Move(profile.Name, offset), profile.Name);
        }

        private void ApplySelected()
        {
            ProfileToApply = SelectedProfile;
            if (ProfileToApply != null) DialogResult = DialogResult.OK;
        }

        /// <summary>
        /// 执行修改并刷新列表；保存失败时提示
        /// </summary>
        private void Run(Action action, string selectName)
        {
            try
            {
                action();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                UITheme.ShowMessage("保存失败：" + ex.Message, "配置方案", MessageBoxIcon.Error);
            }
            Reload(selectName);
        }

        private void ImportProfiles()
        {
            using (var dialog = new OpenFileDialog { Filter = "JSON 文件|*.json" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    List<IpProfile> imported = ProfileManager.ReadFile(dialog.FileName);
                    int conflicts = imported.Count(p => ProfileManager.Find(p.Name) != null);
                    bool overwrite = false;
                    if (conflicts > 0)
                    {
                        DialogResult answer = MessageBox.Show(this,
                            $"有 {conflicts} 个方案与现有方案同名。\n\n是：覆盖同名方案\n否：跳过同名方案\n取消：不导入",
                            "导入配置方案", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                        if (answer == DialogResult.Cancel) return;
                        overwrite = answer == DialogResult.Yes;
                    }

                    ImportResult result = ProfileManager.Import(imported, overwrite);
                    Reload(null);
                    UITheme.ShowMessage($"导入完成：新增 {result.Added} 个，覆盖 {result.Replaced} 个，跳过 {result.Skipped} 个", "导入配置方案");
                }
                catch (SerializationException)
                {
                    UITheme.ShowMessage("文件格式不正确，不是本工具导出的配置方案", "导入配置方案", MessageBoxIcon.Warning);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    UITheme.ShowMessage("导入失败：" + ex.Message, "导入配置方案", MessageBoxIcon.Error);
                }
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
                    UITheme.ShowMessage($"已导出 {ProfileManager.Profiles.Count} 个配置方案", "导出配置方案");
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    UITheme.ShowMessage("导出失败：" + ex.Message, "导出配置方案", MessageBoxIcon.Error);
                }
            }
        }
    }
}
