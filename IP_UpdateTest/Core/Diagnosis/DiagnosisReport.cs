using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace IP_UpdateTest.Core.Diagnosis
{
    /// <summary>
    /// 诊断的环节，按数据包从本机到外网的顺序排列：前面的环节出问题时，后面的失败通常只是连带结果
    /// </summary>
    public enum DiagnosisLayer
    {
        Adapter,
        Gateway,
        Internet,
        Dns,
        ProxyVpn,
        Udp
    }

    /// <summary>
    /// 检查项结果，数值越大越严重（Skipped 除外）
    /// </summary>
    public enum CheckStatus
    {
        Skipped,
        Ok,
        Info,
        Warning,
        Fail
    }

    /// <summary>
    /// 一项检查：结论、细节与处理建议
    /// </summary>
    public sealed class DiagnosisCheck
    {
        public DiagnosisCheck(DiagnosisLayer layer, string title, CheckStatus status, string detail, params string[] suggestions)
        {
            Layer = layer;
            Title = title;
            Status = status;
            Detail = detail ?? "";
            Suggestions = suggestions.Where(s => !string.IsNullOrEmpty(s)).ToList();
        }

        public DiagnosisLayer Layer { get; private set; }
        public string Title { get; private set; }
        public CheckStatus Status { get; private set; }
        public string Detail { get; private set; }
        public List<string> Suggestions { get; private set; }
    }

    /// <summary>
    /// 分层网络诊断报告
    /// </summary>
    public sealed class DiagnosisReport
    {
        public DateTime GeneratedAt { get; set; }

        /// <summary>
        /// 诊断的网卡名称，没有网卡时为空
        /// </summary>
        public string AdapterName { get; set; }

        public List<DiagnosisCheck> Checks { get; } = new List<DiagnosisCheck>();

        /// <summary>
        /// 最先出现失败的环节；全部未失败时为 null
        /// </summary>
        public DiagnosisLayer? FaultLayer
        {
            get
            {
                DiagnosisCheck fail = Checks.FirstOrDefault(c => c.Status == CheckStatus.Fail);
                return fail == null ? (DiagnosisLayer?)null : fail.Layer;
            }
        }

        public static string LayerName(DiagnosisLayer layer)
        {
            switch (layer)
            {
                case DiagnosisLayer.Adapter: return "本机网卡";
                case DiagnosisLayer.Gateway: return "路由器";
                case DiagnosisLayer.Internet: return "外网";
                case DiagnosisLayer.Dns: return "DNS";
                case DiagnosisLayer.ProxyVpn: return "代理/VPN";
                case DiagnosisLayer.Udp: return "UDP";
                default: return layer.ToString();
            }
        }

        /// <summary>
        /// 状态符号。都选 GBK 中有的字符，命令行在中文代码页下也能正常显示
        /// </summary>
        public static string Symbol(CheckStatus status)
        {
            switch (status)
            {
                case CheckStatus.Ok: return "√";
                case CheckStatus.Info: return "·";
                case CheckStatus.Warning: return "!";
                case CheckStatus.Fail: return "×";
                default: return "-";
            }
        }

        /// <summary>
        /// 一个环节的总体状态：取最严重的一项（Info 视为正常）
        /// </summary>
        public CheckStatus LayerStatus(DiagnosisLayer layer)
        {
            List<CheckStatus> statuses = Checks.Where(c => c.Layer == layer).Select(c => c.Status).ToList();
            if (statuses.Count == 0 || statuses.All(s => s == CheckStatus.Skipped)) return CheckStatus.Skipped;
            CheckStatus worst = statuses.Max();
            return worst == CheckStatus.Info ? CheckStatus.Ok : worst;
        }

        /// <summary>
        /// 一句话结论
        /// </summary>
        public string Summary
        {
            get
            {
                DiagnosisLayer? fault = FaultLayer;
                if (fault.HasValue)
                {
                    DiagnosisCheck first = Checks.First(c => c.Status == CheckStatus.Fail);
                    return $"问题出在【{LayerName(fault.Value)}】环节：{first.Detail}";
                }
                int warnings = Checks.Count(c => c.Status == CheckStatus.Warning);
                return warnings == 0 ? "网络正常，未发现问题" : $"可以上网，但有 {warnings} 项需要注意";
            }
        }

        /// <summary>
        /// 优先处理的建议：最先失败环节的失败项，没有失败时取各警告项，去重
        /// </summary>
        public List<string> KeySuggestions
        {
            get
            {
                DiagnosisLayer? fault = FaultLayer;
                IEnumerable<DiagnosisCheck> source = fault.HasValue
                    ? Checks.Where(c => c.Layer == fault.Value && c.Status == CheckStatus.Fail)
                    : Checks.Where(c => c.Status == CheckStatus.Warning);
                return source.SelectMany(c => c.Suggestions).Distinct().ToList();
            }
        }

        public string ToText()
        {
            var sb = new StringBuilder();
            sb.Append($"网络诊断报告    {GeneratedAt:yyyy-MM-dd HH:mm:ss}");
            if (!string.IsNullOrEmpty(AdapterName)) sb.Append("    网卡：" + AdapterName);
            sb.AppendLine();

            var layers = ((DiagnosisLayer[])Enum.GetValues(typeof(DiagnosisLayer))).Where(l => Checks.Any(c => c.Layer == l)).ToList();
            sb.AppendLine("链路：" + string.Join(" → ", layers.Select(l => LayerName(l) + " " + Symbol(LayerStatus(l)))));
            sb.AppendLine();
            sb.AppendLine("结论：" + Summary);
            List<string> suggestions = KeySuggestions;
            if (suggestions.Count > 0)
            {
                sb.AppendLine("建议：");
                for (int i = 0; i < suggestions.Count; i++) sb.AppendLine($"  {i + 1}. {suggestions[i]}");
            }

            int index = 0;
            foreach (DiagnosisLayer layer in layers)
            {
                sb.AppendLine();
                sb.AppendLine($"[{++index}] {LayerName(layer)}  {Symbol(LayerStatus(layer))}");
                foreach (DiagnosisCheck check in Checks.Where(c => c.Layer == layer))
                {
                    sb.AppendLine($"  {Symbol(check.Status)} {check.Title}：{check.Detail}");
                    foreach (string s in check.Suggestions) sb.AppendLine("      → " + s);
                }
            }
            sb.AppendLine();
            sb.AppendLine("说明：√ 正常  · 信息  ! 需注意  × 失败  - 已跳过");
            return sb.ToString();
        }

        public string ToJson()
        {
            var data = new DiagnosisReportData
            {
                GeneratedAt = GeneratedAt.ToString("s"),
                Adapter = AdapterName,
                FaultLayer = FaultLayer?.ToString(),
                Summary = Summary,
                Suggestions = KeySuggestions.ToArray(),
                Checks = Checks.Select(c => new DiagnosisCheckData
                {
                    Layer = c.Layer.ToString(),
                    Title = c.Title,
                    Status = c.Status.ToString(),
                    Detail = c.Detail,
                    Suggestions = c.Suggestions.ToArray()
                }).ToArray()
            };
            var serializer = new DataContractJsonSerializer(typeof(DiagnosisReportData));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, data);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }

    [DataContract]
    public sealed class DiagnosisReportData
    {
        [DataMember(Order = 1)] public string GeneratedAt { get; set; }
        [DataMember(Order = 2)] public string Adapter { get; set; }
        [DataMember(Order = 3)] public string FaultLayer { get; set; }
        [DataMember(Order = 4)] public string Summary { get; set; }
        [DataMember(Order = 5)] public string[] Suggestions { get; set; }
        [DataMember(Order = 6)] public DiagnosisCheckData[] Checks { get; set; }
    }

    [DataContract]
    public sealed class DiagnosisCheckData
    {
        [DataMember(Order = 1)] public string Layer { get; set; }
        [DataMember(Order = 2)] public string Title { get; set; }
        [DataMember(Order = 3)] public string Status { get; set; }
        [DataMember(Order = 4)] public string Detail { get; set; }
        [DataMember(Order = 5)] public string[] Suggestions { get; set; }
    }
}
