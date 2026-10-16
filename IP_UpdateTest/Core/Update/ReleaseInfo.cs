using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;

namespace IP_UpdateTest.Core.Update
{
    /// <summary>
    /// GitHub 上的一个正式版本：版本号、说明和要下载的文件
    /// </summary>
    public sealed class ReleaseInfo
    {
        public Version Version { get; set; }
        public string Tag { get; set; }
        public string Notes { get; set; }
        public DateTime? PublishedAt { get; set; }

        /// <summary>
        /// 发布页（浏览器打开）
        /// </summary>
        public string PageUrl { get; set; }

        public string PackageName { get; set; }
        public string PackageUrl { get; set; }
        public long PackageSize { get; set; }

        /// <summary>
        /// SHA256SUMS.txt；没有时为 null（不允许应用内更新）
        /// </summary>
        public string ChecksumUrl { get; set; }

        public string DisplayVersion
        {
            get { return Versions.Display(Version); }
        }

        /// <summary>
        /// 解析 GitHub API 返回的 release JSON；草稿、预发布、版本号无法识别或没有安装包时返回 null
        /// </summary>
        public static ReleaseInfo FromJson(string json)
        {
            GitHubRelease release = JsonFile.Deserialize<GitHubRelease>(json);
            if (release == null || release.Draft || release.Prerelease) return null;
            Version version = Versions.Parse(release.TagName);
            if (version == null) return null;

            List<GitHubAsset> assets = release.Assets ?? new List<GitHubAsset>();
            string expected = "IPTool-" + release.TagName + ".zip";
            GitHubAsset package = assets.FirstOrDefault(a => string.Equals(a.Name, expected, StringComparison.OrdinalIgnoreCase))
                ?? assets.FirstOrDefault(a => a.Name != null && a.Name.StartsWith("IPTool-", StringComparison.OrdinalIgnoreCase)
                    && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (package == null || string.IsNullOrEmpty(package.DownloadUrl)) return null;
            GitHubAsset sums = assets.FirstOrDefault(a => string.Equals(a.Name, "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase));

            DateTime published;
            return new ReleaseInfo
            {
                Version = version,
                Tag = release.TagName,
                Notes = release.Body ?? "",
                PublishedAt = DateTime.TryParse(release.PublishedAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out published)
                    ? published.ToLocalTime()
                    : (DateTime?)null,
                PageUrl = release.HtmlUrl,
                PackageName = package.Name,
                PackageUrl = package.DownloadUrl,
                PackageSize = package.Size,
                ChecksumUrl = sums == null ? null : sums.DownloadUrl
            };
        }

        [DataContract]
        internal sealed class GitHubRelease
        {
            [DataMember(Name = "tag_name")] public string TagName { get; set; }
            [DataMember(Name = "body")] public string Body { get; set; }
            [DataMember(Name = "html_url")] public string HtmlUrl { get; set; }
            [DataMember(Name = "published_at")] public string PublishedAt { get; set; }
            [DataMember(Name = "draft")] public bool Draft { get; set; }
            [DataMember(Name = "prerelease")] public bool Prerelease { get; set; }
            [DataMember(Name = "assets")] public List<GitHubAsset> Assets { get; set; }
        }

        [DataContract]
        internal sealed class GitHubAsset
        {
            [DataMember(Name = "name")] public string Name { get; set; }
            [DataMember(Name = "size")] public long Size { get; set; }
            [DataMember(Name = "browser_download_url")] public string DownloadUrl { get; set; }
        }
    }

    /// <summary>
    /// 版本号：标签 v2.3.0 ↔ 程序集版本 2.3.0.0，只比较前三段
    /// </summary>
    public static class Versions
    {
        public static Version Parse(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return null;
            string text = tag.Trim().TrimStart('v', 'V');
            Match m = Regex.Match(text, @"^(\d+)\.(\d+)(?:\.(\d+))?$");
            if (!m.Success) return null;
            int build = m.Groups[3].Success ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
            return new Version(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), build);
        }

        public static Version Normalize(Version v)
        {
            return v == null ? null : new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }

        public static bool IsNewer(Version candidate, Version current)
        {
            return candidate != null && current != null && Normalize(candidate) > Normalize(current);
        }

        public static string Display(Version v)
        {
            v = Normalize(v);
            return v == null ? "" : "v" + v.ToString(3);
        }

        /// <summary>
        /// 正在运行的程序版本
        /// </summary>
        public static Version Running
        {
            get { return Normalize(typeof(Versions).Assembly.GetName().Version); }
        }
    }

    public enum NoteKind
    {
        Heading,
        Bullet,
        Text
    }

    public sealed class NoteLine
    {
        public NoteLine(NoteKind kind, string text, int indent = 0)
        {
            Kind = kind;
            Text = text;
            Indent = indent;
        }

        public NoteKind Kind { get; private set; }
        public string Text { get; private set; }
        public int Indent { get; private set; }
    }

    /// <summary>
    /// 把 Release 说明（CHANGELOG 小节 + GitHub 自动生成的变更列表）整理成标题、要点和段落。
    /// 只保留 CHANGELOG 部分：“What's Changed”和“Full Changelog”是给开发者看的
    /// </summary>
    public static class ReleaseNotes
    {
        public static List<NoteLine> Parse(string markdown)
        {
            var lines = new List<NoteLine>();
            foreach (string raw in (markdown ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.TrimEnd();
                string trimmed = line.TrimStart();
                if (trimmed.Length == 0 || trimmed.StartsWith("<!--", StringComparison.Ordinal)) continue;
                if (Regex.IsMatch(trimmed, @"^#+\s*What's Changed", RegexOptions.IgnoreCase)
                    || trimmed.StartsWith("**Full Changelog**", StringComparison.OrdinalIgnoreCase)
                    || Regex.IsMatch(trimmed, @"^#+\s*New Contributors", RegexOptions.IgnoreCase))
                    break;

                Match heading = Regex.Match(trimmed, @"^#{1,6}\s+(.*)$");
                if (heading.Success)
                {
                    string text = Inline(heading.Groups[1].Value);
                    if (text.Length > 0) lines.Add(new NoteLine(NoteKind.Heading, text));
                    continue;
                }
                Match bullet = Regex.Match(line, @"^(\s*)[-*+]\s+(.*)$");
                if (bullet.Success)
                {
                    int indent = bullet.Groups[1].Value.Replace("\t", "  ").Length >= 2 ? 1 : 0;
                    lines.Add(new NoteLine(NoteKind.Bullet, Inline(bullet.Groups[2].Value), indent));
                    continue;
                }
                lines.Add(new NoteLine(NoteKind.Text, Inline(trimmed)));
            }
            return lines;
        }

        /// <summary>
        /// 去掉行内标记：粗体、斜体、代码、链接只留文字
        /// </summary>
        internal static string Inline(string text)
        {
            text = Regex.Replace(text, @"!\[[^\]]*\]\([^)]*\)", "");
            text = Regex.Replace(text, @"\[([^\]]+)\]\([^)]*\)", "$1");
            text = text.Replace("**", "").Replace("__", "").Replace("`", "");
            text = Regex.Replace(text, @"(?<![\w*])\*(?!\s)([^*]+?)\*(?![\w*])", "$1");
            return text.Trim();
        }
    }

    /// <summary>
    /// SHA256SUMS.txt：每行“哈希  文件名”（sha256sum 格式，文件名前可能有 *）
    /// </summary>
    public static class Checksums
    {
        public static string Find(string sums, string fileName)
        {
            foreach (string raw in (sums ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim().TrimStart('﻿');
                Match m = Regex.Match(line, @"^([0-9a-fA-F]{64})\s+\*?(.+)$");
                if (m.Success && string.Equals(m.Groups[2].Value.Trim(), fileName, StringComparison.OrdinalIgnoreCase))
                    return m.Groups[1].Value.ToLowerInvariant();
            }
            return null;
        }

        public static string ToHex(byte[] hash)
        {
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }
}
