using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace IP_UpdateTest.Core
{
    /// <summary>
    /// 以管理员身份重启前暂存的表单内容，新实例启动后回填，避免用户重新输入
    /// </summary>
    [DataContract]
    public class PendingEdit
    {
        /// <summary>
        /// 超过该时长的暂存内容视为过期，不再回填
        /// </summary>
        private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(2);

        private static readonly string FilePath = Path.Combine(Path.GetTempPath(), "IPTool", "pending.json");

        [DataMember] public string AdapterId { get; set; }
        [DataMember] public bool IsDhcp { get; set; }
        [DataMember] public bool ManualDns { get; set; }
        [DataMember] public string IpAddress { get; set; }
        [DataMember] public string SubnetMask { get; set; }
        [DataMember] public string Gateway { get; set; }
        [DataMember] public string DnsMain { get; set; }
        [DataMember] public string DnsBackup { get; set; }
        [DataMember] public DateTime SavedAt { get; set; }

        public void Save()
        {
            SavedAt = DateTime.Now;
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            using (var stream = File.Create(FilePath))
            {
                new DataContractJsonSerializer(typeof(PendingEdit)).WriteObject(stream, this);
            }
        }

        /// <summary>
        /// 读取并删除暂存内容；不存在、已过期或已损坏时返回 null
        /// </summary>
        public static PendingEdit Take()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;

                PendingEdit edit;
                using (var stream = File.OpenRead(FilePath))
                {
                    edit = (PendingEdit)new DataContractJsonSerializer(typeof(PendingEdit)).ReadObject(stream);
                }
                Clear();

                var age = DateTime.Now - edit.SavedAt;
                return age >= TimeSpan.Zero && age <= MaxAge ? edit : null;
            }
            catch (Exception ex) when (ex is IOException || ex is SerializationException || ex is UnauthorizedAccessException)
            {
                Clear();
                return null;
            }
        }

        public static void Clear()
        {
            try
            {
                File.Delete(FilePath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
