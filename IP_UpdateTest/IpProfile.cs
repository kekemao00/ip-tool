using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

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

        public IpProfile()
        {
            CreateTime = DateTime.Now;
        }

        public override string ToString() => Name;
    }

    /// <summary>
    /// 配置方案管理器
    /// </summary>
    public static class ProfileManager
    {
        private static readonly string ProfilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "IPTool", "profiles.json");

        private static List<IpProfile> _profiles;

        /// <summary>
        /// 预设 DNS 列表
        /// </summary>
        public static readonly Dictionary<string, string[]> PresetDns = new Dictionary<string, string[]>
        {
            { "114 DNS", new[] { "114.114.114.114", "114.114.115.115" } },
            { "阿里 DNS", new[] { "223.5.5.5", "223.6.6.6" } },
            { "腾讯 DNS", new[] { "119.29.29.29", "182.254.116.116" } },
            { "百度 DNS", new[] { "180.76.76.76", "" } },
            { "Google DNS", new[] { "8.8.8.8", "8.8.4.4" } },
            { "Cloudflare", new[] { "1.1.1.1", "1.0.0.1" } }
        };

        public static List<IpProfile> Profiles
        {
            get
            {
                if (_profiles == null) Load();
                return _profiles;
            }
        }

        /// <summary>
        /// 加载配置
        /// </summary>
        public static void Load()
        {
            _profiles = new List<IpProfile>();
            try
            {
                if (File.Exists(ProfilePath))
                {
                    var json = File.ReadAllText(ProfilePath, Encoding.UTF8);
                    _profiles = Deserialize<List<IpProfile>>(json) ?? new List<IpProfile>();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载配置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 保存配置
        /// </summary>
        public static void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(ProfilePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(ProfilePath, Serialize(_profiles), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"保存配置失败: {ex.Message}", "错误",
                    System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 添加配置方案
        /// </summary>
        public static void Add(IpProfile profile)
        {
            // 同名覆盖
            _profiles.RemoveAll(p => p.Name == profile.Name);
            _profiles.Add(profile);
            Save();
        }

        /// <summary>
        /// 删除配置方案
        /// </summary>
        public static void Remove(string name)
        {
            _profiles.RemoveAll(p => p.Name == name);
            Save();
        }

        /// <summary>
        /// 导出到文件
        /// </summary>
        public static void Export(string filePath)
        {
            File.WriteAllText(filePath, Serialize(_profiles), Encoding.UTF8);
        }

        /// <summary>
        /// 从文件导入
        /// </summary>
        public static int Import(string filePath)
        {
            var json = File.ReadAllText(filePath, Encoding.UTF8);
            var imported = Deserialize<List<IpProfile>>(json);
            if (imported == null) return 0;

            int count = 0;
            foreach (var p in imported)
            {
                if (!_profiles.Exists(x => x.Name == p.Name))
                {
                    _profiles.Add(p);
                    count++;
                }
            }
            Save();
            return count;
        }

        private static string Serialize<T>(T obj)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, obj);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static T Deserialize<T>(string json)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return (T)serializer.ReadObject(ms);
            }
        }
    }
}
