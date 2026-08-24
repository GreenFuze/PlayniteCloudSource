using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace CloudSource.Playnite.Providers.OneDrive
{
    internal static class OneDriveJson
    {
        public static byte[] Serialize<T>(T value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                return stream.ToArray();
            }
        }

        public static T Deserialize<T>(byte[] data) where T : class
        {
            if (data == null || data.Length == 0) throw new ArgumentException("JSON data is required.", nameof(data));
            using (var stream = new MemoryStream(data, writable: false))
            {
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
            }
        }

        public static T Deserialize<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("JSON is required.", nameof(json));
            return Deserialize<T>(Encoding.UTF8.GetBytes(json));
        }
    }
}
