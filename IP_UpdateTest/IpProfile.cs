using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using IP_UpdateTest.Core;

namespace IP_UpdateTest
{
    /// <summary>
    /// IP 配置方案
    /// </summary>
    [DataContract]
    public class IpProfile
    {
        [DataMember] public string Name { get; set; }
        [DataMember] public string IpAddress { get; set; }
        [DataMember] public string SubnetMask { get; set; }
        [DataMember] public string Gateway { get; set; }
        [DataMember] public string DnsMain { get; set; }
        [DataMember] public string DnsBackup { get; set; }
        [DataMember] public bool IsDhcp { get; set; }
        [DataMember] public DateTime CreateTime { get; set; }

        /// <summary>
        /// DHCP 方案是否手动指定 DNS。旧版本保存的方案没有此项（null），
        /// 当时 DHCP 方案会把 DNS 恢复为自动获取，读取时保持这一行为。
        /// </summary>
        [DataMember] public bool? ManualDns { get; set; }

        /// <summary>
        /// 绑定的网卡 MAC 地址（如 74-56-3C-12-AB-CD），为空表示应用到当前选中的网卡
        /// </summary>
        [DataMember] public string AdapterMac { get; set; }

        public IpProfile()
        {
            CreateTime = DateTime.Now;
        }

        public IpProfile Clone()
        {
            var copy = (IpProfile)MemberwiseClone();
            copy.CreateTime = DateTime.Now;
            return copy;
        }

        /// <summary>
        /// 列表中显示的配置摘要，如“静态 192.168.1.10 / 网关 192.168.1.1”
        /// </summary>
        public string Summary
        {
            get
            {
                string ip = IsDhcp ? "DHCP" : $"静态 {IpAddress}";
                string dns = string.Join(", ", IpConfigRequest.DnsList(DnsMain, DnsBackup));
                bool manualDns = IsDhcp ? ManualDns == true && dns.Length > 0 : dns.Length > 0;
                return ip + (manualDns ? " / DNS " + dns : "");
            }
        }

        /// <summary>
        /// 转成要应用的配置
        /// </summary>
        public IpConfigRequest ToRequest()
        {
            string[] dns = IpConfigRequest.DnsList(DnsMain, DnsBackup);
            if (!IsDhcp) return IpConfigRequest.Static(IpAddress, SubnetMask, Gateway, dns);

            bool manualDns = ManualDns == true && dns.Length > 0;
            return new IpConfigRequest
            {
                UseDhcp = true,
                UseDhcpDns = !manualDns,
                DnsServers = manualDns ? dns : new string[0]
            };
        }

        public override string ToString() => Name;
    }

    /// <summary>
    /// 导入配置方案的结果
    /// </summary>
    public sealed class ImportResult
    {
        public int Added { get; set; }
        public int Replaced { get; set; }
        public int Skipped { get; set; }
    }

    /// <summary>
    /// 配置方案管理器。读写失败时抛出异常，由界面负责提示。
    /// </summary>
    public static class ProfileManager
    {
        private static string _profilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "IPTool", "profiles.json");

        private static List<IpProfile> _profiles;

        /// <summary>
        /// 读取失败（如文件被占用）时禁止保存，避免用空列表覆盖已有数据
        /// </summary>
        private static bool _readFailed;

        /// <summary>
        /// 预设 DNS 列表。腾讯云官方文档只给出 119.29.29.29 一个 IPv4 地址。
        /// </summary>
        public static readonly Dictionary<string, string[]> PresetDns = new Dictionary<string, string[]>
        {
            { "114 DNS", new[] { "114.114.114.114", "114.114.115.115" } },
            { "阿里 DNS", new[] { "223.5.5.5", "223.6.6.6" } },
            { "腾讯 DNS", new[] { "119.29.29.29", "" } },
            { "百度 DNS", new[] { "180.76.76.76", "" } },
            { "Google DNS", new[] { "8.8.8.8", "8.8.4.4" } },
            { "Cloudflare", new[] { "1.1.1.1", "1.0.0.1" } }
        };

        /// <summary>
        /// 读取时遇到的问题，界面应提示一次；没有问题时为 null
        /// </summary>
        public static string LoadWarning { get; private set; }

        public static List<IpProfile> Profiles
        {
            get
            {
                if (_profiles == null) Load();
                return _profiles;
            }
        }

        /// <summary>
        /// 改用指定的存储文件（测试用）
        /// </summary>
        public static void UseStorePath(string path)
        {
            _profilePath = path;
            _profiles = null;
        }

        /// <summary>
        /// 加载配置
        /// </summary>
        public static void Load()
        {
            _profiles = new List<IpProfile>();
            _readFailed = false;
            LoadWarning = null;
            try
            {
                List<IpProfile> loaded = JsonFile.Read<List<IpProfile>>(_profilePath);
                if (loaded != null) _profiles = loaded.Where(p => p != null && !string.IsNullOrWhiteSpace(p.Name)).ToList();
            }
            catch (SerializationException)
            {
                // 文件损坏：先改名备份，免得下次保存把它覆盖掉
                string backup = Path.ChangeExtension(_profilePath, ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".json");
                try
                {
                    File.Move(_profilePath, backup);
                    LoadWarning = $"配置方案文件已损坏，已备份为 {backup}";
                }
                catch (IOException)
                {
                    _readFailed = true;
                    LoadWarning = "配置方案文件已损坏，且无法备份";
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _readFailed = true;
                LoadWarning = "读取配置方案失败：" + ex.Message;
            }
        }

        /// <summary>
        /// 保存配置
        /// </summary>
        public static void Save()
        {
            if (_readFailed) throw new IOException("配置方案文件读取失败，为避免覆盖已有数据，本次不保存");
            JsonFile.WriteAtomic(_profilePath, Profiles);
        }

        public static IpProfile Find(string name)
        {
            int index = IndexOf(name);
            return index >= 0 ? Profiles[index] : null;
        }

        /// <summary>
        /// 方案名称比较时忽略大小写和首尾空格
        /// </summary>
        public static bool SameName(string a, string b)
        {
            return string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.CurrentCultureIgnoreCase);
        }

        /// <summary>
        /// 添加配置方案；已有同名方案时原位替换
        /// </summary>
        public static void Add(IpProfile profile)
        {
            int index = IndexOf(profile.Name);
            if (index >= 0) Profiles[index] = profile;
            else Profiles.Add(profile);
            Save();
        }

        /// <summary>
        /// 修改配置方案（可改名）；新名称与其他方案重名时抛出 ArgumentException
        /// </summary>
        public static void Update(string originalName, IpProfile profile)
        {
            int index = IndexOf(originalName);
            int duplicate = IndexOf(profile.Name);
            if (duplicate >= 0 && duplicate != index)
                throw new ArgumentException($"已存在名为“{profile.Name}”的配置方案");

            if (index >= 0) Profiles[index] = profile;
            else Profiles.Add(profile);
            Save();
        }

        /// <summary>
        /// 删除配置方案
        /// </summary>
        public static void Remove(string name)
        {
            Profiles.RemoveAll(p => SameName(p.Name, name));
            Save();
        }

        /// <summary>
        /// 调整顺序，offset 为 -1 上移、1 下移
        /// </summary>
        public static void Move(string name, int offset)
        {
            int index = IndexOf(name);
            int target = index + offset;
            if (index < 0 || target < 0 || target >= Profiles.Count) return;

            IpProfile profile = Profiles[index];
            Profiles[index] = Profiles[target];
            Profiles[target] = profile;
            Save();
        }

        /// <summary>
        /// 导出到文件
        /// </summary>
        public static void Export(string filePath)
        {
            File.WriteAllText(filePath, JsonFile.Serialize(Profiles), new UTF8Encoding(false));
        }

        /// <summary>
        /// 读取导入文件；格式不对时抛出 SerializationException
        /// </summary>
        public static List<IpProfile> ReadFile(string filePath)
        {
            List<IpProfile> imported = JsonFile.Deserialize<List<IpProfile>>(File.ReadAllText(filePath, Encoding.UTF8));
            return (imported ?? new List<IpProfile>()).Where(p => p != null && !string.IsNullOrWhiteSpace(p.Name)).ToList();
        }

        /// <summary>
        /// 导入方案；overwrite 为 true 时覆盖同名方案，否则跳过
        /// </summary>
        public static ImportResult Import(IEnumerable<IpProfile> imported, bool overwrite)
        {
            var result = new ImportResult();
            foreach (IpProfile profile in imported)
            {
                int index = IndexOf(profile.Name);
                if (index < 0)
                {
                    Profiles.Add(profile);
                    result.Added++;
                }
                else if (overwrite)
                {
                    Profiles[index] = profile;
                    result.Replaced++;
                }
                else
                {
                    result.Skipped++;
                }
            }
            if (result.Added + result.Replaced > 0) Save();
            return result;
        }

        private static int IndexOf(string name)
        {
            return Profiles.FindIndex(p => SameName(p.Name, name));
        }
    }
}
