using System;
using System.Collections.Generic;
using System.Linq;

namespace IP_UpdateTest.Core.TerminalProxy
{
    /// <summary>
    /// “key = value” 格式的配置文件（.npmrc、pip.ini）：只读写已有的代理项，其余内容原样保留
    /// </summary>
    public static class ConfigLines
    {
        /// <summary>
        /// 读取各键的值（同名取最后一个，与 npm、pip 一致）；注释行跳过
        /// </summary>
        public static Dictionary<string, string> Read(string text, IEnumerable<string> keys)
        {
            var wanted = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in Split(text))
            {
                string key, value;
                if (TryParse(line, out key, out value) && wanted.Contains(key) && value.Length > 0) values[key] = value;
            }
            return values;
        }

        /// <summary>
        /// 把已有的 key 改成 value（value 为 null 时删除该行），返回新文本
        /// </summary>
        public static string Update(string text, string key, string value)
        {
            var output = new List<string>();
            foreach (string line in Split(text))
            {
                string k, v;
                if (TryParse(line, out k, out v) && string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                {
                    if (value == null) continue;
                    int eq = line.IndexOf('=');
                    string spacing = eq > 0 && line[eq - 1] == ' ' ? " = " : "=";
                    output.Add(line.Substring(0, line.Length - line.TrimStart().Length) + k + spacing + value);
                    continue;
                }
                output.Add(line);
            }
            string newline = text != null && text.Contains("\r\n") ? "\r\n" : "\n";
            return string.Join(newline, output);
        }

        private static IEnumerable<string> Split(string text)
        {
            return (text ?? "").Replace("\r\n", "\n").Split('\n');
        }

        private static bool TryParse(string line, out string key, out string value)
        {
            key = value = null;
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#' || trimmed[0] == ';' || trimmed[0] == '[') return false;
            int eq = trimmed.IndexOf('=');
            if (eq <= 0) return false;
            key = trimmed.Substring(0, eq).Trim();
            value = trimmed.Substring(eq + 1).Trim().Trim('"');
            return true;
        }
    }
}
