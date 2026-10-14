using System;
using System.IO;
using UnityEngine;

namespace VMS.Backend
{
    /// <summary>Small JSON files in persistentDataPath (saves, caches).</summary>
    public static class JsonFile
    {
        public static T Read<T>(string path) where T : class
        {
            try
            {
                return File.Exists(path) ? JsonUtility.FromJson<T>(File.ReadAllText(path)) : null;
            }
            catch (Exception e)
            {
                Debug.LogError("[JsonFile] Failed to read " + path + ": " + e.Message);
                return null;
            }
        }

        /// <summary>Write-then-rename so a crash mid-write never leaves a truncated file.</summary>
        public static void Write(string path, object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(value));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public static void Delete(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
