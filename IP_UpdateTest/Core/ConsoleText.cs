using System.Runtime.InteropServices;
using System.Text;

namespace IP_UpdateTest.Core
{
    /// <summary>
    /// 控制台程序（netsh 等）输出的解码
    /// </summary>
    public static class ConsoleText
    {
        [DllImport("kernel32.dll")]
        private static extern uint GetOEMCP();

        /// <summary>
        /// 系统 OEM 代码页（中文系统为 936；开启“使用 UTF-8 提供全球语言支持”后为 65001）
        /// </summary>
        public static Encoding OemEncoding
        {
            get { return Encoding.GetEncoding((int)GetOEMCP()); }
        }

        /// <summary>
        /// 控制台程序按所在控制台的代码页输出：从窗口程序启动时是 OEM 代码页，
        /// 从 UTF-8 控制台（如 PowerShell 7、Windows Terminal）启动时是 UTF-8。
        /// 先按 UTF-8 严格解码，不合法再按 OEM 代码页解码（GBK 中文几乎不可能恰好是合法 UTF-8）。
        /// </summary>
        public static string Decode(byte[] bytes)
        {
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return OemEncoding.GetString(bytes);
            }
        }
    }
}
