using System;
using System.IO;
using UnityEngine;

namespace UniverseSimAR.Utils {
    /// <summary>
    /// Đọc/ghi cấu hình dạng JSON trong Application.persistentDataPath.
    /// Dùng được cho mọi class có [Serializable] (chỉ các field public hoặc [SerializeField]).
    /// </summary>
    public static class ConfigStore {
        private static string PathFor(string name) {
            return Path.Combine(Application.persistentDataPath, name + ".json");
        }

        public static bool Exists(string name) {
            return File.Exists(PathFor(name));
        }

        /// <summary>
        /// Đọc cấu hình. Field nào thiếu trong file sẽ giữ giá trị của `defaults`
        /// (nên thêm field mới ở bản cập nhật sau không làm hỏng file cũ).
        /// Lỗi đọc/hỏng file -> trả về `defaults`.
        /// </summary>
        public static T Load<T>(string name, T defaults = null) where T : class, new() {
            T obj = defaults ?? new T();
            string path = PathFor(name);

            try {
                if (!File.Exists(path)) return obj;

                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return obj;

                JsonUtility.FromJsonOverwrite(json, obj);
            }
            catch (Exception e) {
                Debug.LogWarning($"[ConfigStore] Load '{name}' failed, using defaults: {e.Message}");
            }
            return obj;
        }

        /// <summary>Ghi cấu hình (ghi file tạm rồi đổi tên để không hỏng file nếu app bị tắt giữa chừng).</summary>
        public static bool Save<T>(string name, T obj) where T : class {
            string path = PathFor(name);
            string tmp = path + ".tmp";

            try {
                File.WriteAllText(tmp, JsonUtility.ToJson(obj, true));
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                return true;
            }
            catch (Exception e) {
                Debug.LogWarning($"[ConfigStore] Save '{name}' failed: {e.Message}");
                return false;
            }
        }

        public static void Delete(string name) {
            try {
                string path = PathFor(name);
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e) {
                Debug.LogWarning($"[ConfigStore] Delete '{name}' failed: {e.Message}");
            }
        }
    }

    /// <summary>Cấu hình chế độ chia đôi màn hình VR.</summary>
    [Serializable]
    public class VRConfig {
        public int version = 1;
        public float ipdMm = 63f;
        public float eyeWidth = 0.5f;
        public float zoom = 1f;
        public float resolutionScale = 0.8f;
        public float screenWidthMmOverride = 0f;
    }
}