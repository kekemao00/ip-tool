using System;
using System.IO;
using System.Linq;
using System.Text;
using IP_UpdateTest.Core;
using Xunit;

namespace IP_UpdateTest.Tests
{
    /// <summary>
    /// 配置方案存储。ProfileManager 是静态类，这里每个用例都改用临时目录中的文件。
    /// </summary>
    public class ProfileManagerTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "IPToolTests", Guid.NewGuid().ToString("N"));

        public ProfileManagerTests()
        {
            Directory.CreateDirectory(directory);
            ProfileManager.UseStorePath(StorePath);
        }

        private string StorePath
        {
            get { return Path.Combine(directory, "profiles.json"); }
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch (IOException)
            {
            }
        }

        private static IpProfile Static(string name, string ip)
        {
            return new IpProfile { Name = name, IpAddress = ip, SubnetMask = "255.255.255.0", Gateway = "", DnsMain = "", DnsBackup = "" };
        }

        [Fact]
        public void AddReplaceMoveRemove_PersistAcrossReload()
        {
            ProfileManager.Add(Static("公司", "10.0.0.5"));
            ProfileManager.Add(Static("家里", "192.168.1.5"));
            ProfileManager.Add(Static("公司", "10.0.0.6"));   // 同名原位替换
            ProfileManager.Move("家里", -1);

            ProfileManager.Load();

            Assert.Equal(new[] { "家里", "公司" }, ProfileManager.Profiles.Select(p => p.Name));
            Assert.Equal("10.0.0.6", ProfileManager.Find("公司").IpAddress);
            Assert.False(File.Exists(StorePath + ".tmp")); // 原子写入后不留临时文件

            ProfileManager.Remove("公司");
            ProfileManager.Load();
            Assert.Single(ProfileManager.Profiles);
        }

        [Fact]
        public void Names_IgnoreCaseAndSpaces()
        {
            ProfileManager.Add(Static("Office", "10.0.0.5"));

            Assert.NotNull(ProfileManager.Find(" office "));
        }

        [Fact]
        public void Update_RenameToExistingName_Throws()
        {
            ProfileManager.Add(Static("公司", "10.0.0.5"));
            ProfileManager.Add(Static("家里", "192.168.1.5"));

            Assert.Throws<ArgumentException>(() => ProfileManager.Update("家里", Static("公司", "192.168.1.5")));
        }

        [Fact]
        public void Import_SkipsOrOverwritesDuplicates()
        {
            ProfileManager.Add(Static("公司", "10.0.0.5"));
            var imported = new[] { Static("公司", "10.0.0.9"), Static("实验室", "172.16.0.5") };

            ImportResult skipped = ProfileManager.Import(imported, false);
            Assert.Equal(1, skipped.Added);
            Assert.Equal(1, skipped.Skipped);
            Assert.Equal("10.0.0.5", ProfileManager.Find("公司").IpAddress);

            // 第一次导入后“实验室”也已存在，这次两个都是同名覆盖
            ImportResult replaced = ProfileManager.Import(imported, true);
            Assert.Equal(2, replaced.Replaced);
            Assert.Equal(0, replaced.Added);
            Assert.Equal("10.0.0.9", ProfileManager.Find("公司").IpAddress);
        }

        [Fact]
        public void Load_OldVersionFile_StaysCompatible()
        {
            // v1.1.0 保存的格式：没有 ManualDns、AdapterMac，DHCP 方案也存了当时显示的 DNS
            File.WriteAllText(StorePath,
                "[{\"CreateTime\":\"\\/Date(1735545600000+0800)\\/\",\"DnsBackup\":\"\",\"DnsMain\":\"192.168.3.1\","
                + "\"Gateway\":\"\",\"IpAddress\":\"\",\"IsDhcp\":true,\"Name\":\"家里\",\"SubnetMask\":\"\"}]",
                new UTF8Encoding(true));

            ProfileManager.Load();
            IpProfile profile = ProfileManager.Find("家里");

            Assert.Null(ProfileManager.LoadWarning);
            Assert.Null(profile.ManualDns);
            Assert.Null(profile.AdapterMac);
            Assert.True(profile.ToRequest().UseDhcpDns); // 与旧版本行为一致：DNS 自动获取
        }

        [Fact]
        public void Load_CorruptFile_IsBackedUpInsteadOfOverwritten()
        {
            File.WriteAllText(StorePath, "{ 这不是 JSON");

            ProfileManager.Load();

            Assert.Contains("已损坏", ProfileManager.LoadWarning);
            Assert.Empty(ProfileManager.Profiles);
            Assert.False(File.Exists(StorePath));
            Assert.Single(Directory.GetFiles(directory, "profiles.corrupt-*.json"));
        }
    }

    public class AppSettingsTests
    {
        [Fact]
        public void Deserialize_MissingMembers_UsesDefaults()
        {
            AppSettings settings = JsonFile.Deserialize<AppSettings>("{}");

            Assert.False(settings.MinimizeToTray);
            Assert.Empty(settings.CustomDnsPresets);
        }

        [Fact]
        public void RoundTrip()
        {
            var settings = new AppSettings { MinimizeToTray = true, ShowAllAdapters = true };
            settings.CustomDnsPresets.Add(new DnsPreset { Name = "公司 DNS", Servers = new[] { "10.0.0.53" } });

            AppSettings copy = JsonFile.Deserialize<AppSettings>(JsonFile.Serialize(settings));

            Assert.True(copy.MinimizeToTray);
            Assert.Equal("公司 DNS", copy.CustomDnsPresets.Single().Name);
        }
    }
}
