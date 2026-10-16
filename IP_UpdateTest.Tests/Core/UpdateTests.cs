using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using IP_UpdateTest.Core.Update;
using Xunit;

namespace IP_UpdateTest.Tests.Core
{
    public class VersionsTests
    {
        [Theory]
        [InlineData("v2.3.0", "2.3.0")]
        [InlineData("2.3.1", "2.3.1")]
        [InlineData("V3.0", "3.0.0")]
        public void Parse_AcceptsReleaseTags(string tag, string expected)
        {
            Assert.Equal(new Version(expected), Versions.Parse(tag));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("latest")]
        [InlineData("v2.3.0-beta.1")]
        public void Parse_RejectsOthers(string tag)
        {
            Assert.Null(Versions.Parse(tag));
        }

        [Fact]
        public void IsNewer_ComparesFirstThreeParts()
        {
            Assert.True(Versions.IsNewer(new Version(2, 3, 0), new Version(2, 2, 2, 0)));
            Assert.True(Versions.IsNewer(new Version(2, 10, 0), new Version(2, 9, 9)));
            Assert.False(Versions.IsNewer(new Version(2, 2, 2), new Version(2, 2, 2, 0)));
            Assert.False(Versions.IsNewer(new Version(2, 2, 1), new Version(2, 2, 2)));
            Assert.False(Versions.IsNewer(null, new Version(2, 2, 2)));
        }

        [Fact]
        public void Display_UsesTagFormat()
        {
            Assert.Equal("v2.2.2", Versions.Display(new Version(2, 2, 2, 0)));
        }
    }

    public class ReleaseInfoTests
    {
        private const string Json = @"{
  ""tag_name"": ""v2.3.0"",
  ""name"": ""v2.3.0"",
  ""draft"": false,
  ""prerelease"": false,
  ""html_url"": ""https://github.com/kekemao00/ip-tool/releases/tag/v2.3.0"",
  ""published_at"": ""2026-10-20T08:00:00Z"",
  ""body"": ""### 新功能\n\n- 应用内更新"",
  ""assets"": [
    { ""name"": ""SHA256SUMS.txt"", ""size"": 85, ""browser_download_url"": ""https://example.com/SHA256SUMS.txt"" },
    { ""name"": ""IPTool-v2.3.0.zip"", ""size"": 452000, ""browser_download_url"": ""https://example.com/IPTool-v2.3.0.zip"" }
  ]
}";

        [Fact]
        public void FromJson_PicksPackageAndChecksums()
        {
            ReleaseInfo release = ReleaseInfo.FromJson(Json);

            Assert.Equal(new Version(2, 3, 0), release.Version);
            Assert.Equal("v2.3.0", release.Tag);
            Assert.Equal("IPTool-v2.3.0.zip", release.PackageName);
            Assert.Equal(452000, release.PackageSize);
            Assert.Equal("https://example.com/IPTool-v2.3.0.zip", release.PackageUrl);
            Assert.Equal("https://example.com/SHA256SUMS.txt", release.ChecksumUrl);
            Assert.Equal(new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc), release.PublishedAt.Value.ToUniversalTime());
            Assert.Contains("应用内更新", release.Notes);
        }

        [Fact]
        public void FromJson_SkipsPrereleaseAndMissingPackage()
        {
            Assert.Null(ReleaseInfo.FromJson(Json.Replace(@"""prerelease"": false", @"""prerelease"": true")));
            Assert.Null(ReleaseInfo.FromJson(Json.Replace("IPTool-v2.3.0.zip", "source.tar.gz")));
        }

        [Fact]
        public void FromJson_NoChecksumFile()
        {
            ReleaseInfo release = ReleaseInfo.FromJson(Json.Replace("SHA256SUMS.txt", "notes.txt"));

            Assert.NotNull(release);
            Assert.Null(release.ChecksumUrl);
        }
    }

    public class ReleaseNotesTests
    {
        [Fact]
        public void Parse_KeepsChangelogAndDropsGeneratedList()
        {
            const string body = "### 新功能\n\n- 命令面板：按 **Ctrl+K** 搜索\n  - 子项 `code`\n\n### 改进\n\n- 见 [README](https://example.com)\n\n\n## What's Changed\n* 某个 PR by @someone in https://github.com/x/y/pull/1\n\n**Full Changelog**: https://github.com/x/y/compare/a...b";

            List<NoteLine> lines = ReleaseNotes.Parse(body);

            Assert.Equal(new[] { NoteKind.Heading, NoteKind.Bullet, NoteKind.Bullet, NoteKind.Heading, NoteKind.Bullet }, lines.Select(l => l.Kind).ToArray());
            Assert.Equal("新功能", lines[0].Text);
            Assert.Equal("命令面板：按 Ctrl+K 搜索", lines[1].Text);
            Assert.Equal(1, lines[2].Indent);
            Assert.Equal("子项 code", lines[2].Text);
            Assert.Equal("见 README", lines[4].Text);
        }

        [Fact]
        public void Parse_PlainParagraph()
        {
            List<NoteLine> lines = ReleaseNotes.Parse("界面按新的设计重做。\r\n");

            Assert.Single(lines);
            Assert.Equal(NoteKind.Text, lines[0].Kind);
        }
    }

    public class ChecksumsTests
    {
        [Fact]
        public void Find_MatchesFileName()
        {
            const string hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789ABCDEF";
            string sums = "﻿" + hash + "  IPTool-v2.3.0.zip\r\nffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff *other.zip\r\n";

            Assert.Equal(hash.ToLowerInvariant(), Checksums.Find(sums, "IPTool-v2.3.0.zip"));
            Assert.Equal(new string('f', 64), Checksums.Find(sums, "other.zip"));
            Assert.Null(Checksums.Find(sums, "missing.zip"));
        }
    }

    public class UpdateInstallerTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "IPToolTests", Guid.NewGuid().ToString("N"));
        private readonly string source;
        private readonly string target;

        public UpdateInstallerTests()
        {
            source = Path.Combine(root, "source");
            target = Path.Combine(root, "target");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(target);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch (IOException)
            {
            }
        }

        [Fact]
        public void Install_ReplacesFilesAndKeepsBackup()
        {
            File.WriteAllText(Path.Combine(target, "IPTool.exe"), "old");
            File.WriteAllText(Path.Combine(target, "profiles.json"), "user data");
            File.WriteAllText(Path.Combine(source, "IPTool.exe"), "new");
            File.WriteAllText(Path.Combine(source, "IPTool.exe.config"), "config");

            UpdateInstaller.Install(source, target);

            Assert.Equal("new", File.ReadAllText(Path.Combine(target, "IPTool.exe")));
            Assert.Equal("config", File.ReadAllText(Path.Combine(target, "IPTool.exe.config")));
            Assert.Equal("old", File.ReadAllText(Path.Combine(target, "IPTool.exe.old")));
            Assert.Equal("user data", File.ReadAllText(Path.Combine(target, "profiles.json")));
        }

        [Fact]
        public void Install_FailureRestoresOriginals()
        {
            File.WriteAllText(Path.Combine(target, "IPTool.exe"), "old");
            File.WriteAllText(Path.Combine(source, "IPTool.exe"), "new");
            File.WriteAllText(Path.Combine(source, "IPTool.exe.config"), "config");
            File.WriteAllText(Path.Combine(target, "IPTool.exe.config"), "old config");

            // 第二个文件被其他程序独占打开，不能改名：已替换的第一个文件要恢复
            using (new FileStream(Path.Combine(target, "IPTool.exe.config"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.ThrowsAny<IOException>(() => UpdateInstaller.Install(source, target));
            }

            Assert.Equal("old", File.ReadAllText(Path.Combine(target, "IPTool.exe")));
            Assert.Equal("old config", File.ReadAllText(Path.Combine(target, "IPTool.exe.config")));
            Assert.False(File.Exists(Path.Combine(target, "IPTool.exe.old")));
        }

        [Fact]
        public void CleanupBackups_RemovesOnlyOldProgramFiles()
        {
            foreach (string name in new[] { "IPTool.exe.old", "IPTool.exe.old1", "IPTool.exe.config.old", "IPTool.exe", "IPTool.old.txt", "notes.old" })
                File.WriteAllText(Path.Combine(target, name), "");

            UpdateInstaller.CleanupBackups(target);

            Assert.Equal(new[] { "IPTool.exe", "IPTool.old.txt", "notes.old" }, Directory.GetFiles(target).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        }

        [Fact]
        public void Extract_RejectsPathsOutsideFolder()
        {
            string zip = Path.Combine(root, "evil.zip");
            using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("../evil.txt").Open())) writer.Write("x");
            }

            Assert.Throws<UpdateException>(() => UpdateClient.Extract(zip, Path.Combine(root, "out")));
            Assert.False(File.Exists(Path.Combine(root, "evil.txt")));
        }

        [Fact]
        public void Extract_FlatPackage()
        {
            string zip = Path.Combine(root, "ok.zip");
            using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("IPTool.exe").Open())) writer.Write("exe");
                using (var writer = new StreamWriter(archive.CreateEntry("IPTool.exe.config").Open())) writer.Write("config");
            }
            string output = Path.Combine(root, "out");

            UpdateClient.Extract(zip, output);

            Assert.Equal("exe", File.ReadAllText(Path.Combine(output, "IPTool.exe")));
            Assert.Equal("config", File.ReadAllText(Path.Combine(output, "IPTool.exe.config")));
            Assert.Equal(Checksums.ToHex(System.Security.Cryptography.SHA256.Create().ComputeHash(File.ReadAllBytes(zip))), UpdateClient.HashFile(zip));
        }
    }
}
