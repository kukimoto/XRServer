using System.IO;
using System.Text.Json;

namespace XRServer
{
    /// <summary>
    /// XRServer 同梱用シークレット設定。
    /// - secrets.json は wwwroot の外に置く（今回は setting フォルダ）
    /// - 値はログに出さない（APIキー漏洩防止）
    /// </summary>
    public sealed class AppSecrets
    {
        public string? GEMINI_API_KEY { get; set; }
        public string? GEMINI_MODEL { get; set; }

        /// <summary>
        /// クライアントがアクセスする起点URL（例: http://192.168.3.10:1800）
        /// </summary>
        public string? APP_ORIGIN { get; set; }

        /// <summary>
        /// 指定ディレクトリ配下の secrets.json を読み込む。
        /// secrets.json が無い場合でもサーバ自体は起動できるようにする。
        /// </summary>
        public static AppSecrets LoadFromDir(string dir)
        {
            var path = Path.Combine(dir, "secrets.json");
            if (!File.Exists(path))
            {
                return new AppSecrets();
            }

            var json = File.ReadAllText(path);
            var secrets = JsonSerializer.Deserialize<AppSecrets>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return secrets ?? new AppSecrets();
        }

        public static void SaveToDir(string dir, AppSecrets secrets)
        {
            Directory.CreateDirectory(dir);

            var path = Path.Combine(dir, "secrets.json");
            var json = JsonSerializer.Serialize(secrets, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
    }
}