using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace IP_UpdateTest.Core
{
    /// <summary>
    /// JSON 文件读写（DataContract 格式，与旧版本保存的文件兼容）
    /// </summary>
    public static class JsonFile
    {
        public static string Serialize<T>(T value)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, value);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        public static T Deserialize<T>(string json)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return (T)serializer.ReadObject(ms);
            }
        }

        /// <summary>
        /// 读取文件，不存在时返回默认值；文件损坏时抛出 SerializationException
        /// </summary>
        public static T Read<T>(string path)
        {
            if (!File.Exists(path)) return default(T);
            return Deserialize<T>(File.ReadAllText(path, Encoding.UTF8));
        }

        /// <summary>
        /// 先写临时文件再替换原文件，避免写到一半（如断电）时把原文件弄坏
        /// </summary>
        public static void WriteAtomic<T>(string path, T value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            File.WriteAllText(temp, Serialize(value), new UTF8Encoding(false));

            if (!File.Exists(path))
            {
                File.Move(temp, path);
                return;
            }
            try
            {
                File.Replace(temp, path, null);
            }
            catch (IOException)
            {
                // 部分文件系统（如网络共享）不支持替换，退回覆盖复制
                File.Copy(temp, path, true);
                File.Delete(temp);
            }
        }
    }
}
