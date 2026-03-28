using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace XRServer
{
    /// <summary>
    /// setting\appsettings.json / setting\secrets.json を GUI で編集するダイアログ。
    /// - 開いた時点の値を表示
    /// - OK で入力値を Result に格納して DialogResult.OK を返す
    /// - 保存/サーバ再起動は Form1 側で行う
    /// </summary>
    public sealed partial class Setting : Form
    {
        private readonly ServerConfig _config;
        private readonly AppSecrets _secrets;

        public SettingResult? Result { get; private set; }

        public Setting(ServerConfig currentConfig, AppSecrets currentSecrets)
        {
            InitializeComponent();

            // 参照のままだとダイアログ中の編集で本体に影響するので clone
            _config = Clone(currentConfig);
            _secrets = Clone(currentSecrets);

            this.Shown += (_, __) => LoadToUi();

            // Closeボタン（Designer上のボタン名が Close）
            Close.Click += Close_Click;

            // クリックでファイル/フォルダ選択（仕様）
            wwwroot.Click += (_, __) => BrowseFolder(wwwroot, "WWW ルートディレクトリを選択");
            StoraegeDir.Click += (_, __) => BrowseFolder(StoraegeDir, "データ保存ディレクトリを選択");
            Phproot.Click += (_, __) => BrowsePhpCgiExe(Phproot);

            // パス系は ReadOnly にして誤入力を減らす（必要なら外してOK）
            wwwroot.ReadOnly = true;
            StoraegeDir.ReadOnly = true;
            Phproot.ReadOnly = true;

            // Enter=OK / Esc=Close
            this.AcceptButton = OK;
            this.CancelButton = Close;
        }

        private static ServerConfig Clone(ServerConfig c)
        {
            return new ServerConfig
            {
                Port = c.Port,
                Url = c.Url,
                StorageDir = c.StorageDir,
                AllowedCidrs = (c.AllowedCidrs ?? new List<string>()).ToList(),
                WebRoot = c.WebRoot,
                PhpRoot = c.PhpRoot,
                PhpCgiPath = c.PhpCgiPath
            };
        }

        private static AppSecrets Clone(AppSecrets s)
        {
            return new AppSecrets
            {
                GEMINI_API_KEY = s.GEMINI_API_KEY,
                GEMINI_MODEL = s.GEMINI_MODEL,
                APP_ORIGIN = s.APP_ORIGIN
            };
        }

        private void LoadToUi()
        {
            int port = _config.Port > 0 ? _config.Port : TryGetPortFromUrl(_config.Url, 1800);

            portNo.Text = port.ToString();
            wwwroot.Text = _config.WebRoot ?? "";
            Phproot.Text = _config.PhpCgiPath ?? "";      // ← php-cgi.exe パス
            StoraegeDir.Text = _config.StorageDir ?? "";
            AllowedCidrs.Text = string.Join(", ", _config.AllowedCidrs ?? new List<string>());

            GEMINI_API_KEY.Text = _secrets.GEMINI_API_KEY ?? "";
            GEMINI_MODEL.Text = _secrets.GEMINI_MODEL ?? "";
        }

        private static int TryGetPortFromUrl(string? url, int fallback)
        {
            if (string.IsNullOrWhiteSpace(url)) return fallback;

            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Port > 0)
                return uri.Port;

            var m = System.Text.RegularExpressions.Regex.Match(url, @":(\d{1,5})\b");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var p) && p >= 1 && p <= 65535)
                return p;

            return fallback;
        }

        private void BrowseFolder(TextBox target, string title)
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = title,
                UseDescriptionForTitle = true,
                SelectedPath = Directory.Exists(target.Text) ? target.Text : AppContext.BaseDirectory
            };

            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                target.Text = dlg.SelectedPath;
            }
        }

        private void BrowsePhpCgiExe(TextBox target)
        {
            using var dlg = new OpenFileDialog
            {
                Title = "php-cgi.exe を選択",
                Filter = "php-cgi.exe|php-cgi.exe|Executable (*.exe)|*.exe|All files (*.*)|*.*",
                CheckFileExists = true,
                CheckPathExists = true,
                FileName = "php-cgi.exe",
                InitialDirectory = Directory.Exists(Path.GetDirectoryName(target.Text))
                    ? Path.GetDirectoryName(target.Text)
                    : AppContext.BaseDirectory
            };

            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                target.Text = dlg.FileName;
            }
        }

        private void OK_Click(object sender, EventArgs e)
        {
            if (!TryBuildResult(out var result, out var error))
            {
                MessageBox.Show(this, error, "Invalid Setting", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Result = result;
            DialogResult = DialogResult.OK;
            Close(); // Form.Close()
        }

        private void Close_Click(object? sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close(); // Form.Close()
        }

        private bool TryBuildResult(out SettingResult result, out string error)
        {
            result = new SettingResult();
            error = "";

            // Port
            if (!int.TryParse(portNo.Text.Trim(), out var port) || port < 1 || port > 65535)
            {
                error = "ポート番号は 1〜65535 の整数で入力してください。";
                return false;
            }

            // WebRoot
            var webRoot = (wwwroot.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(webRoot))
            {
                error = "WWW ルートディレクトリが空です。";
                return false;
            }

            // php-cgi.exe path（TextBox名は Phproot）
            var phpCgiPath = (Phproot.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(phpCgiPath))
            {
                error = "php-cgi.exe のパスが空です。";
                return false;
            }
            if (!File.Exists(phpCgiPath))
            {
                error = $"php-cgi.exe が見つかりません。\r\n{phpCgiPath}";
                return false;
            }

            // StorageDir
            var storageDir = (StoraegeDir.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(storageDir))
            {
                error = "データベース(保存)ディレクトリが空です。";
                return false;
            }

            // AllowedCidrs（カンマ区切り）
            var raw = (AllowedCidrs.Text ?? "").Trim();
            var cidrs = raw.Split(',')
                           .Select(x => x.Trim())
                           .Where(x => !string.IsNullOrWhiteSpace(x))
                           .ToList();

            if (cidrs.Count == 0)
            {
                error = "許可されたIP範囲が空です。例: 192.168.3.0/24";
                return false;
            }

            // CIDR形式をバリデーション（無効なものは弾く）
            foreach (var c in cidrs)
            {
                if (!CidrRule.TryParse(c, out _))
                {
                    error = $"CIDR形式が不正です: {c}\r\n例: 192.168.3.0/24";
                    return false;
                }
            }

            // 絶対パス化（仕様4）
            webRoot = ToAbsolutePath(webRoot);
            storageDir = ToAbsolutePath(storageDir);
            phpCgiPath = ToAbsolutePath(phpCgiPath);

            result.Port = port;
            result.WebRoot = webRoot;
            result.StorageDir = storageDir;
            result.PhpCgiPath = phpCgiPath;
            result.AllowedCidrs = cidrs;

            result.GEMINIApiKey = (GEMINI_API_KEY.Text ?? "").Trim();
            result.GEMINIModel = (GEMINI_MODEL.Text ?? "").Trim();

            return true;
        }

        private static string ToAbsolutePath(string path)
        {
            if (Path.IsPathRooted(path)) return Path.GetFullPath(path);

            // 相対入力された場合は exe 基準に解釈（ただし保存は絶対パス）
            var baseDir = AppContext.BaseDirectory;
            return Path.GetFullPath(Path.Combine(baseDir, path));
        }
    }

    public sealed class SettingResult
    {
        public int Port { get; set; }
        public string WebRoot { get; set; } = "";
        public string PhpCgiPath { get; set; } = "";
        public string StorageDir { get; set; } = "";
        public List<string> AllowedCidrs { get; set; } = new();

        public string GEMINIApiKey { get; set; } = "";
        public string GEMINIModel { get; set; } = "";
    }
}