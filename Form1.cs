using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PointCloudConvert;
using DWGtoPDF;

namespace XRServer
{
    public partial class Form1 : Form
    {
        private WebApplication? _app;
        private readonly CancellationTokenSource _cts = new();
        private ServerConfig _config = ServerConfig.CreateDefault();

        // Gemini u铽lv(setting\secrets.json)
        private AppSecrets _secrets = new AppSecrets();

        private const string SettingsFolderName = "setting";

        public Form1()
        {
            InitializeComponent();

            // UIꍇɂLi݊j
            // - Button  Name: btnSelectStorage
            // - TextBox Name: txtStorageDir (ReadOnly)
            TryHookOptionalUi();

            this.Shown += async (_, __) =>
            {
                try
                {
                    LoadConfig();     // setting\appsettings.json
                    LoadSecrets();    // setting\secrets.jsoni΍쐬j
                    UpdateAddressLabel();

                    ApplyConfigToOptionalUi();
                    await StartWebServerAsync();
                    UpdateTitle();
                }
                catch (Exception ex)
                {
                    LogError("Server start failed", ex);
                }
            };

            this.FormClosing += async (_, __) =>
            {
                await StopWebServerAsync();
                _cts.Cancel();
                _cts.Dispose();
            };

            var opt = new PointCloudConvertOptions();
            //Console.WriteLine(opt.MaxDepth);
            AppendConsole($"MaxDepth: {opt.MaxDepth}");

            TestPointCloudConvert();
            LogRequiredDwgPdfDependencies();

            Address.Text = "";
        }

        // -----------------------------
        // ݒtH_ / ݒt@CpX
        // -----------------------------
        internal static string GetSettingsDir()
        {
            var baseDir = AppContext.BaseDirectory;
            return Path.Combine(baseDir, SettingsFolderName);
        }

        internal static string GetConfigPath() => Path.Combine(GetSettingsDir(), "appsettings.json");
        internal static string GetSecretsPath() => Path.Combine(GetSettingsDir(), "secrets.json");

        // -----------------------------
        // UIOiConsole TextBoxցj
        // -----------------------------
        private void LogInfo(string message) => AppendConsole($"[INFO] {Timestamp()} {message}");
        private void LogWarn(string message) => AppendConsole($"[WARN] {Timestamp()} {message}");

        private void LogError(string message, Exception ex)
        {
            AppendConsole($"[ERROR] {Timestamp()} {message}");
            AppendConsole(ex.ToString());
        }

        private static string Timestamp() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

        private void AppendConsole(string line)
        {
            if (IsDisposed) return;

            void write() => Console.AppendText(line + Environment.NewLine);

            if (InvokeRequired) BeginInvoke((Action)write);
            else write();
        }

        private void LogRequiredDwgPdfDependencies()
        {
            var baseDir = AppContext.BaseDirectory;
            LogInfo($"Checking DWG/PDF converter dependencies in: {baseDir}");

            foreach (var fileName in new[] { "DWGtoPDF.dll", "CADimportNet.dll" })
            {
                var fullPath = Path.Combine(baseDir, fileName);
                if (File.Exists(fullPath))
                    LogInfo($"FOUND: {fileName}");
                else
                    LogWarn($"MISSING: {fileName}");
            }
        }

        private static bool IsLoopbackRequest(HttpContext ctx)
        {
            var remote = ctx.Connection.RemoteIpAddress;
            if (remote == null) return false;
            if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();
            return IPAddress.IsLoopback(remote);
        }

        private string GetAnnoLinkProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(_config.WebRoot, "ANNOLINK", "AnnoLinkProject"));
        }

        private static bool IsPathUnderRoot(string rootPath, string targetPath)
        {
            var fullRoot = Path.GetFullPath(rootPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            var fullTarget = Path.GetFullPath(targetPath);
            return fullTarget.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }

        private void ConvertDwgToPdfInternal(string inputPath, string outputPath, string bgMode)
        {
            var normalizedBgMode = string.IsNullOrWhiteSpace(bgMode) ? "black" : bgMode.Trim().ToLowerInvariant();
            if (normalizedBgMode != "black" && normalizedBgMode != "white")
                throw new ArgumentException("bgMode must be 'black' or 'white'.", nameof(bgMode));

            LogRequiredDwgPdfDependencies();

            DwgPdfConverter.Convert(
                inputPath,
                outputPath,
                normalizedBgMode,
                msg => LogInfo("[DWG] " + msg)
            );
        }

        // -----------------------------
        // LAN IP i\/APP_ORIGINpj
        // -----------------------------
        private static IPAddress? GetBestLanIPv4()
        {
            var candidates = new List<(IPAddress ip, int score)>();

            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                try
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    var ipProps = ni.GetIPProperties();
                    foreach (var ua in ipProps.UnicastAddresses)
                    {
                        var ip = ua.Address;
                        if (ip.AddressFamily != AddressFamily.InterNetwork) continue;

                        int score = IsPrivateIPv4(ip) ? 100 : 10;
                        if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet) score += 30;
                        if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) score += 20;

                        var desc = ni.Description ?? "";
                        if (desc.Contains("Virtual", StringComparison.OrdinalIgnoreCase)) score -= 30;

                        candidates.Add((ip, score));
                    }
                }
                catch
                {
                    // ignore
                }
            }

            if (candidates.Count == 0) return null;

            return candidates
                .OrderByDescending(x => x.score)
                .ThenBy(x => x.ip.ToString(), StringComparer.Ordinal)
                .Select(x => x.ip)
                .FirstOrDefault();
        }

        private static bool IsPrivateIPv4(IPAddress ip)
        {
            var b = ip.GetAddressBytes();
            if (b[0] == 10) return true;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
            if (b[0] == 192 && b[1] == 168) return true;
            return false;
        }

        private void UpdateAddressLabel()
        {
            var ip = GetBestLanIPv4() ?? IPAddress.Loopback;
            Address.Text = $"http://{ip}:{_config.Port}/";
        }

        private string BuildClientOrigin(int port)
        {
            var ip = GetBestLanIPv4() ?? IPAddress.Loopback;
            return $"http://{ip}:{port}";
        }

        private static int GetPortFromUrl(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                if (uri.Port > 0) return uri.Port;
            }

            var m = Regex.Match(url ?? "", @":(\d{1,5})\b");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var p) && p >= 1 && p <= 65535)
                return p;

            return 1800;
        }

        private static string BuildBindUrl(int port) => $"http://0.0.0.0:{port}";

        // -----------------------------
        // ݒisetting\appsettings.jsonj
        // -----------------------------
        private void LoadConfig()
        {
            var settingsDir = GetSettingsDir();
            Directory.CreateDirectory(settingsDir);

            var path = GetConfigPath();

            try
            {
                if (!File.Exists(path))
                {
                    _config = ServerConfig.CreateDefault();
                    NormalizeConfigPaths(_config);
                    SaveConfig();
                    LogInfo($"Created default config: {path}");
                }
                else
                {
                    var text = File.ReadAllText(path);
                    var cfg = JsonSerializer.Deserialize<ServerConfig>(text);
                    _config = cfg ?? ServerConfig.CreateDefault();
                    NormalizeConfigPaths(_config);
                    LogInfo($"Loaded config: {path}");
                }

                if (_config.Port <= 0)
                    _config.Port = GetPortFromUrl(_config.Url);

                // AāFbind  0.0.0.0 Œ
                _config.Url = BuildBindUrl(_config.Port);

                // PhpRoot ͏ WebRoot Ɠ
                _config.PhpRoot = _config.WebRoot;

                // Ő΃pX
                NormalizeConfigPaths(_config);

                Directory.CreateDirectory(_config.StorageDir);

                LogInfo($"Url={_config.Url}");
                LogInfo($"Port={_config.Port}");
                LogInfo($"StorageDir={_config.StorageDir}");
                LogInfo($"AllowedCidrs={string.Join(", ", _config.AllowedCidrs ?? new List<string>())}");
                LogInfo($"WebRoot={_config.WebRoot}");
                LogInfo($"PhpRoot={_config.PhpRoot}");
                LogInfo($"PhpCgiPath={_config.PhpCgiPath}");
            }
            catch (Exception ex)
            {
                _config = ServerConfig.CreateDefault();
                NormalizeConfigPaths(_config);
                Directory.CreateDirectory(_config.StorageDir);
                LogError("Failed to load config. Fallback to default.", ex);
            }
        }

        private void SaveConfig()
        {
            var settingsDir = GetSettingsDir();
            Directory.CreateDirectory(settingsDir);

            if (_config.Port <= 0) _config.Port = 1800;
            _config.Url = BuildBindUrl(_config.Port);
            _config.PhpRoot = _config.WebRoot;

            NormalizeConfigPaths(_config);

            var path = GetConfigPath();
            var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);

            LogInfo($"Saved config: {path}");
        }

        private static void NormalizeConfigPaths(ServerConfig cfg)
        {
            var baseDir = AppContext.BaseDirectory;

            if (string.IsNullOrWhiteSpace(cfg.StorageDir))
                cfg.StorageDir = Path.Combine(baseDir, "data");
            if (!Path.IsPathRooted(cfg.StorageDir))
                cfg.StorageDir = Path.Combine(baseDir, cfg.StorageDir);

            if (string.IsNullOrWhiteSpace(cfg.WebRoot))
                cfg.WebRoot = Path.Combine(baseDir, "wwwroot");
            if (!Path.IsPathRooted(cfg.WebRoot))
                cfg.WebRoot = Path.Combine(baseDir, cfg.WebRoot);

            cfg.PhpRoot = cfg.WebRoot;

            if (string.IsNullOrWhiteSpace(cfg.PhpCgiPath))
                cfg.PhpCgiPath = Path.Combine(baseDir, @"php\php-cgi.exe");
            if (!Path.IsPathRooted(cfg.PhpCgiPath))
                cfg.PhpCgiPath = Path.Combine(baseDir, cfg.PhpCgiPath);
        }

        // -----------------------------
        // Secretsisetting\secrets.jsonj
        // -----------------------------
        private void LoadSecrets()
        {
            try
            {
                var settingsDir = GetSettingsDir();
                Directory.CreateDirectory(settingsDir);

                var secretsPath = GetSecretsPath();
                if (!File.Exists(secretsPath))
                {
                    _secrets = new AppSecrets();
                    AppSecrets.SaveToDir(settingsDir, _secrets);
                    LogInfo($"Created default secrets: {secretsPath}");
                }
                else
                {
                    _secrets = AppSecrets.LoadFromDir(settingsDir);
                    LogInfo($"Loaded secrets: {secretsPath}");
                }

                // APP_ORIGIN ͏ɁuNCAgN_URLvɑ
                var desired = BuildClientOrigin(_config.Port);
                if (!string.Equals(_secrets.APP_ORIGIN ?? "", desired, StringComparison.OrdinalIgnoreCase))
                {
                    _secrets.APP_ORIGIN = desired;
                    AppSecrets.SaveToDir(settingsDir, _secrets);
                    LogInfo($"Updated APP_ORIGIN={desired}");
                }

                if (string.IsNullOrWhiteSpace(_secrets.GEMINI_API_KEY))
                    LogWarn("secrets.json loaded, but GEMINI_API_KEY is missing.");
                else
                    LogInfo("secrets.json loaded (GEMINI_API_KEY=SET).");
            }
            catch (Exception ex)
            {
                _secrets = new AppSecrets();
                LogWarn("Failed to load secrets.json. Gemini polish will be disabled.");
                LogError("LoadSecrets failed", ex);
            }
        }

        private void SaveSecrets()
        {
            var settingsDir = GetSettingsDir();
            Directory.CreateDirectory(settingsDir);
            AppSecrets.SaveToDir(settingsDir, _secrets);
        }

        private void UpdateTitle()
        {
            Text = $"XRServer - bind:{_config.Url} - Storage:{_config.StorageDir}";
        }

        // -----------------------------
        // ݒ_CAO
        // -----------------------------
        private async void btnSetting_Click(object sender, EventArgs e)
        {
            using var dlg = new Setting(_config, _secrets);
            var dr = dlg.ShowDialog(this);
            if (dr != DialogResult.OK || dlg.Result == null) return;

            await ApplySettingAndRestartAsync(dlg.Result);
        }

        private async Task ApplySettingAndRestartAsync(SettingResult newSetting)
        {
            var oldConfig = JsonSerializer.Deserialize<ServerConfig>(JsonSerializer.Serialize(_config)) ?? ServerConfig.CreateDefault();
            var oldSecrets = JsonSerializer.Deserialize<AppSecrets>(JsonSerializer.Serialize(_secrets)) ?? new AppSecrets();

            await StopWebServerAsync();

            _config.Port = newSetting.Port;
            _config.Url = BuildBindUrl(_config.Port);
            _config.StorageDir = newSetting.StorageDir;
            _config.WebRoot = newSetting.WebRoot;
            _config.PhpRoot = _config.WebRoot;
            _config.PhpCgiPath = newSetting.PhpCgiPath;
            _config.AllowedCidrs = newSetting.AllowedCidrs;

            NormalizeConfigPaths(_config);

            _secrets.GEMINI_API_KEY = newSetting.GEMINIApiKey;
            _secrets.GEMINI_MODEL = newSetting.GEMINIModel;
            _secrets.APP_ORIGIN = BuildClientOrigin(_config.Port);

            Directory.CreateDirectory(_config.StorageDir);
            Directory.CreateDirectory(_config.WebRoot);

            SaveConfig();
            SaveSecrets();

            try
            {
                await StartWebServerAsync();
                UpdateTitle();
                UpdateAddressLabel();
                LogInfo("Settings applied and server restarted.");
            }
            catch (Exception ex)
            {
                LogError("Failed to restart server with new settings. Rolling back.", ex);

                _config = oldConfig;
                _secrets = oldSecrets;

                try { SaveConfig(); SaveSecrets(); } catch { /* ignore */ }

                try
                {
                    await StartWebServerAsync();
                    UpdateTitle();
                    UpdateAddressLabel();
                    LogInfo("Rollback succeeded. Server restarted with previous settings.");
                }
                catch (Exception ex3)
                {
                    LogError("Rollback failed. Server remains stopped.", ex3);
                }

                MessageBox.Show(this,
                    "ݒKpăT[oċNɎs܂B\r\ni|[g/pXs/sȂǁj\r\n̐ݒɖ߂܂B",
                    "Apply Failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        // -----------------------------
        // WebT[oN / ~
        // -----------------------------

        private async Task StartWebServerAsync()
        {
            string baseDir = AppContext.BaseDirectory;

            LogInfo($"baseDir   ={baseDir}");
            LogInfo($"WebRoot   ={_config.WebRoot}");
            LogInfo($"PhpRoot   ={_config.PhpRoot}");
            LogInfo($"StorageDir={_config.StorageDir}");
            LogInfo($"ScenesDir ={GetScenesRoot()}");
            LogInfo($"PhpCgiPath={_config.PhpCgiPath}");
            LogInfo($"Url       ={_config.Url}");

            if (_app != null) return;

            var allowCidrs = (_config.AllowedCidrs ?? new List<string>())
                .Select(s => s?.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            var allowRules = new List<CidrRule>();
            foreach (var cidr in allowCidrs)
            {
                if (CidrRule.TryParse(cidr!, out var rule)) allowRules.Add(rule);
                else LogWarn($"Invalid CIDR ignored: {cidr}");
            }

            allowRules.Add(CidrRule.LoopbackOnly());
            LogInfo("Loopback (127.0.0.1) is always allowed.");

            if (allowRules.Count == 1)
            {
                LogWarn("No valid CIDR found except loopback (127.0.0.1).");
            }

            Directory.CreateDirectory(_config.StorageDir);
            Directory.CreateDirectory(_config.WebRoot);
            Directory.CreateDirectory(_config.PhpRoot);
            Directory.CreateDirectory(GetScenesRoot());

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls(_config.Url);

            // Kestrel: リクエスト本文全体の上限
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Limits.MaxRequestBodySize = 500 * 1024 * 1024; // 500MB
            });

            // multipart/form-data の上限
            builder.Services.Configure<FormOptions>(options =>
            {
                options.MultipartBodyLengthLimit = 600 * 1024 * 1024; // 600MB
            });

            builder.WebHost.UseUrls(_config.Url);

            _app = builder.Build();

            _app.Use(async (ctx, next) =>
            {
                try { await next(); }
                catch (Exception ex)
                {
                    var rip = ctx.Connection.RemoteIpAddress;
                    if (rip is { IsIPv4MappedToIPv6: true }) rip = rip.MapToIPv4();
                    LogError($"Unhandled exception (ip={rip}, path={ctx.Request.Path})", ex);

                    if (!ctx.Response.HasStarted)
                    {
                        ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
                        await ctx.Response.WriteAsync("Internal Server Error");
                    }
                }
            });

            _app.Use(async (ctx, next) =>
            {
                var remote = ctx.Connection.RemoteIpAddress;
                if (remote == null)
                {
                    ctx.Response.StatusCode = 403;
                    await ctx.Response.WriteAsync("Forbidden (no remote ip).");
                    return;
                }
                if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();
                if (remote.AddressFamily != AddressFamily.InterNetwork)
                {
                    ctx.Response.StatusCode = 403;
                    await ctx.Response.WriteAsync("Forbidden (IPv6 not allowed).");
                    return;
                }
                if (!allowRules.Any(r => r.Contains(remote)))
                {
                    ctx.Response.StatusCode = 403;
                    await ctx.Response.WriteAsync($"Forbidden (ip={remote}).");
                    return;
                }
                await next();
            });

            _app.Use(async (ctx, next) =>
            {
                var sw = Stopwatch.StartNew();
                var rip = ctx.Connection.RemoteIpAddress;
                if (rip is { IsIPv4MappedToIPv6: true }) rip = rip.MapToIPv4();

                await next();

                sw.Stop();
                LogInfo($"{rip} {ctx.Request.Method} {ctx.Request.Path} -> {ctx.Response.StatusCode} ({sw.ElapsedMilliseconds}ms)");
            });

            _app.Use(async (ctx, next) =>
            {
                var p = ctx.Request.Path.Value ?? "";
                if (p.StartsWith("/data/", StringComparison.OrdinalIgnoreCase) || p.Contains("/.", StringComparison.Ordinal))
                {
                    ctx.Response.StatusCode = 403;
                    await ctx.Response.WriteAsync("Forbidden");
                    return;
                }
                await next();
            });

            var phpCgiPath = _config.PhpCgiPath;
            if (!File.Exists(phpCgiPath)) LogWarn($"php-cgi.exe NOT FOUND: {phpCgiPath}");

            bool TryMapToPhysical(string urlPath, out string physicalPath)
            {
                physicalPath = "";
                var rel = urlPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);

                var full = Path.GetFullPath(Path.Combine(_config.PhpRoot, rel));
                var rootFull = Path.GetFullPath(_config.PhpRoot);
                if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) return false;

                physicalPath = full;
                return true;
            }

            _app.Use(async (ctx, next) =>
            {
                var path = ctx.Request.Path.Value ?? "/";

                if (path.EndsWith(".php", StringComparison.OrdinalIgnoreCase))
                {
                    await PhpCgiHandler.HandleAsync(
                        ctx,
                        phpCgiPath: _config.PhpCgiPath,
                        documentRoot: _config.PhpRoot,
                        dataDir: _config.StorageDir,
                        secrets: _secrets,
                        urlPrefix: "",
                        log: LogInfo,
                        logWarn: LogWarn,
                        logError: LogError
                    );
                    return;
                }

                bool looksLikeDir = path.EndsWith("/", StringComparison.OrdinalIgnoreCase) || !Path.HasExtension(path);
                if (looksLikeDir)
                {
                    var dirPath = path.EndsWith("/") ? path : path + "/";
                    var indexPhpUrl = dirPath + "index.php";
                    if (TryMapToPhysical(indexPhpUrl, out var indexPhpPhysical) && File.Exists(indexPhpPhysical))
                    {
                        ctx.Request.Path = indexPhpUrl;
                        await PhpCgiHandler.HandleAsync(
                            ctx,
                            phpCgiPath: phpCgiPath,
                            documentRoot: _config.PhpRoot,
                            dataDir: _config.StorageDir,
                            secrets: _secrets,
                            urlPrefix: "",
                            log: LogInfo,
                            logWarn: LogWarn,
                            logError: LogError);
                        return;
                    }
                }

                await next();
            });

            var webrootFull = _config.WebRoot;
            var scenesRoot = GetScenesRoot();

            var contentTypes = new FileExtensionContentTypeProvider();
            contentTypes.Mappings[".vrl"] = "application/json";
            contentTypes.Mappings[".fbx"] = "application/octet-stream";
            contentTypes.Mappings[".glb"] = "model/gltf-binary";
            contentTypes.Mappings[".gltf"] = "model/gltf+json";
            contentTypes.Mappings[".usd"] = "application/octet-stream";
            contentTypes.Mappings[".usda"] = "text/plain";
            contentTypes.Mappings[".usdc"] = "application/octet-stream";
            contentTypes.Mappings[".usdz"] = "model/vnd.usdz+zip";
            contentTypes.Mappings[".usa"] = "application/octet-stream";
            contentTypes.Mappings[".bin"] = "application/octet-stream";
            contentTypes.Mappings[".stl"] = "model/stl";
            contentTypes.Mappings[".obj"] = "text/plain";
            contentTypes.Mappings[".mtl"] = "text/plain";
            contentTypes.Mappings[".dwg"] = "application/octet-stream";
            contentTypes.Mappings[".dxf"] = "application/octet-stream";

            _app.UseDefaultFiles(new DefaultFilesOptions
            {
                FileProvider = new PhysicalFileProvider(webrootFull),
                RequestPath = ""
            });

            _app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(webrootFull),
                RequestPath = "",
                ContentTypeProvider = contentTypes,
            });

            _app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(scenesRoot),
                RequestPath = "/scenes",
                ContentTypeProvider = contentTypes,
            });

            _app.MapGet("/health", () => Results.Text("XRServer OK"));

            _app.MapPost("/internal/convert/dwg-to-pdf", async (HttpRequest req) =>
            {
                if (!IsLoopbackRequest(req.HttpContext))
                    return Results.StatusCode(StatusCodes.Status403Forbidden);

                DwgConvertRequest? dto;
                try
                {
                    dto = await JsonSerializer.DeserializeAsync<DwgConvertRequest>(req.Body);
                }
                catch (Exception ex)
                {
                    LogError("Invalid DWG convert request JSON.", ex);
                    return Results.BadRequest("Invalid JSON.");
                }

                if (dto == null)
                    return Results.BadRequest("Request body is empty.");

                if (string.IsNullOrWhiteSpace(dto.InputPath) || string.IsNullOrWhiteSpace(dto.OutputPath))
                    return Results.BadRequest("InputPath and OutputPath are required.");

                var inputPath = Path.GetFullPath(dto.InputPath);
                var outputPath = Path.GetFullPath(dto.OutputPath);
                var projectRoot = GetAnnoLinkProjectRoot();

                if (!IsPathUnderRoot(projectRoot, inputPath) || !IsPathUnderRoot(projectRoot, outputPath))
                    return Results.BadRequest("Path must be under ANNOLINK/AnnoLinkProject.");

                var inputExt = Path.GetExtension(inputPath).ToLowerInvariant();
                if (inputExt != ".dwg" && inputExt != ".dxf")
                    return Results.BadRequest("InputPath must be .dwg or .dxf.");

                var outputExt = Path.GetExtension(outputPath).ToLowerInvariant();
                if (outputExt != ".pdf")
                    return Results.BadRequest("OutputPath must be .pdf.");

                try
                {
                    LogInfo($"[DWG] Internal convert request: input={inputPath}, output={outputPath}");
                    ConvertDwgToPdfInternal(inputPath, outputPath, dto.BgMode ?? "black");
                    return Results.Json(new { ok = true, outputPath });
                }
                catch (Exception ex)
                {
                    LogError("DWG to PDF conversion failed.", ex);
                    return Results.Problem(
                        title: "DWG to PDF conversion failed",
                        detail: ex.Message,
                        statusCode: StatusCodes.Status500InternalServerError);
                }
            });


           // RegisterXlsxViewerRoutes(_app);

            _app.MapGet("/api/list", () =>
            {
                var dir = _config.StorageDir;
                var files = Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly)
                    .Select(p => new FileInfo(p))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Select(f => new { f.Name, f.Length, LastWriteTimeUtc = f.LastWriteTimeUtc });
                return Results.Json(files);
            });

            _app.MapPost("/api/upload", async (HttpRequest req) =>
            {
                if (!req.HasFormContentType) return Results.BadRequest("Use multipart/form-data.");

                var form = await req.ReadFormAsync();
                var file = form.Files["file"];
                if (file == null || file.Length == 0) return Results.BadRequest("file is empty.");

                var safeName = Path.GetFileName(file.FileName);
                var dst = MakeUniquePath(Path.Combine(_config.StorageDir, safeName));

                await using var fs = File.Create(dst);
                await using var input = file.OpenReadStream();
                await input.CopyToAsync(fs);

                LogInfo($"Uploaded: {Path.GetFileName(dst)} ({file.Length} bytes)");
                return Results.Ok(new { name = Path.GetFileName(dst), size = file.Length });
            });

            _app.MapGet("/api/download/{name}", (string name) =>
            {
                var safeName = Path.GetFileName(name);
                var path = Path.Combine(_config.StorageDir, safeName);
                if (!File.Exists(path)) return Results.NotFound();

                LogInfo($"Download: {safeName}");
                return Results.File(path, "application/octet-stream", fileDownloadName: safeName);
            });

            _app.MapPost("/api/scene/upload", async (HttpRequest req) =>
            {
                if (!req.HasFormContentType)
                    return Results.BadRequest("Use multipart/form-data.");

                var form = await req.ReadFormAsync();
                var file = form.Files["file"] ?? form.Files.FirstOrDefault();
                if (file == null || file.Length == 0)
                    return Results.BadRequest("file is empty.");

                var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant();
                if (ext != ".usdz" && ext != ".glb")
                    return Results.BadRequest("Only .usdz or .glb is supported.");

                var sceneId = Guid.NewGuid().ToString("D");
                var sceneDir = GetSceneDir(sceneId);

                try
                {
                    Directory.CreateDirectory(sceneDir);

                    var modelPath = Path.Combine(sceneDir, "model.glb");
                    var statePath = Path.Combine(sceneDir, "state.usda");

                    if (ext == ".glb")
                    {
                        await using var input = file.OpenReadStream();
                        await using var output = File.Create(modelPath);
                        await input.CopyToAsync(output);

                        USDZConverter.CreateEmptyStateUsda(statePath);
                    }
                    else
                    {
                        var originalUsdzPath = Path.Combine(sceneDir, "original.usdz");
                        await using (var output = File.Create(originalUsdzPath))
                        await using (var input = file.OpenReadStream())
                        {
                            await input.CopyToAsync(output);
                        }

                        USDZConverter.ToGlb(originalUsdzPath, modelPath);
                        USDZConverter.CreateEmptyStateUsda(statePath);
                    }

                    LogInfo($"Scene uploaded: sceneId={sceneId}, src={file.FileName}, size={file.Length}");
                    return Results.Json(new
                    {
                        sceneId,
                        modelUrl = BuildSceneModelUrl(sceneId),
                        stateUrl = BuildSceneStateUrl(sceneId)
                    });
                }
                catch (Exception ex)
                {
                    TryDeleteDirectory(sceneDir);
                    LogError($"Scene upload failed: {file.FileName}", ex);
                    return Results.Problem(
                        title: "Scene upload failed",
                        detail: ex.Message,
                        statusCode: StatusCodes.Status500InternalServerError);
                }
            });

            _app.MapPost("/api/scene/{sceneId}/state", async (string sceneId, HttpRequest req) =>
            {
                if (!IsSafeSceneId(sceneId))
                    return Results.BadRequest("Invalid sceneId.");

                var sceneDir = GetSceneDir(sceneId);
                if (!Directory.Exists(sceneDir))
                    return Results.NotFound();

                var statePath = Path.Combine(sceneDir, "state.usda");

                using var reader = new StreamReader(req.Body, Encoding.UTF8);
                var rawBody = await reader.ReadToEndAsync();
                var stateText = NormalizeStatePayload(rawBody);

                if (string.IsNullOrWhiteSpace(stateText))
                    return Results.BadRequest("Request body is empty.");

                await File.WriteAllTextAsync(statePath, stateText, new UTF8Encoding(false));
                LogInfo($"Scene state updated: sceneId={sceneId}");

                return Results.Json(new { ok = true });
            });

            _app.MapGet("/api/scene/{sceneId}/download", async (string sceneId) =>
            {
                if (!IsSafeSceneId(sceneId))
                    return Results.BadRequest("Invalid sceneId.");

                var sceneDir = GetSceneDir(sceneId);
                var modelPath = Path.Combine(sceneDir, "model.glb");
                var statePath = Path.Combine(sceneDir, "state.usda");

                if (!File.Exists(modelPath) || !File.Exists(statePath))
                    return Results.NotFound();

                var tempUsdzPath = Path.Combine(Path.GetTempPath(), $"scene-{sceneId}-{Guid.NewGuid():N}.usdz");

                try
                {
                    USDZConverter.RepackToUsdz(modelPath, statePath, tempUsdzPath);
                    var bytes = await File.ReadAllBytesAsync(tempUsdzPath);

                    LogInfo($"Scene downloaded as USDZ: sceneId={sceneId}");
                    return Results.File(bytes, "model/vnd.usdz+zip", $"{sceneId}.usdz");
                }
                finally
                {
                    if (File.Exists(tempUsdzPath))
                    {
                        try { File.Delete(tempUsdzPath); } catch { }
                    }
                }
            });

            await _app.StartAsync(_cts.Token);
            LogInfo($"Web server started: {_config.Url}");
        }


        private async Task StopWebServerAsync()
        {
            if (_app == null) return;
            try
            {
                LogInfo("Stopping web server...");
                await _app.StopAsync(TimeSpan.FromSeconds(3));
                LogInfo("Web server stopped.");
            }
            catch (Exception ex)
            {
                LogError("Failed to stop web server.", ex);
            }
            finally
            {
                await _app.DisposeAsync();
                _app = null;
            }
        }



        private string GetScenesRoot()
        {
            return Path.Combine(_config.StorageDir, "scenes");
        }

        private string GetSceneDir(string sceneId)
        {
            return Path.Combine(GetScenesRoot(), sceneId);
        }

        private static bool IsSafeSceneId(string sceneId)
        {
            if (string.IsNullOrWhiteSpace(sceneId) || sceneId.Length > 128) return false;
            return Regex.IsMatch(sceneId, "^[A-Za-z0-9][A-Za-z0-9_-]*$");
        }

        private static string NormalizeStatePayload(string rawBody)
        {
            if (rawBody == null) return "";

            var trimmed = rawBody.Trim();
            if (trimmed.Length == 0) return "";

            if (trimmed.StartsWith("\"", StringComparison.Ordinal) && trimmed.EndsWith("\"", StringComparison.Ordinal))
            {
                try
                {
                    var decoded = JsonSerializer.Deserialize<string>(trimmed);
                    if (decoded != null) return decoded;
                }
                catch
                {
                    // raw text body として扱う
                }
            }

            return rawBody;
        }

        private static void TryDeleteDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;

            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch
            {
                // best effort
            }
        }

        private static string BuildSceneModelUrl(string sceneId) => $"/scenes/{sceneId}/model.glb";
        private static string BuildSceneStateUrl(string sceneId) => $"/scenes/{sceneId}/state.usda";

        private static string MakeUniquePath(string path)
        {
            if (!File.Exists(path)) return path;

            var dir = Path.GetDirectoryName(path) ?? "";
            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);

            for (int i = 1; i < 10_000; i++)
            {
                var p = Path.Combine(dir, $"{name}({i}){ext}");
                if (!File.Exists(p)) return p;
            }
            throw new IOException("Could not create unique file name.");
        }

        // Optional UIi݂ΗLj
        private void TryHookOptionalUi()
        {
            var btn = FindControl<Button>("btnSelectStorage");
            if (btn != null)
            {
                btn.Click += (_, __) => SelectStorageDir();
            }
        }

        private void ApplyConfigToOptionalUi()
        {
            var tb = FindControl<TextBox>("txtStorageDir");
            if (tb != null)
            {
                tb.Text = _config.StorageDir;
            }
        }

        private void SelectStorageDir()
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = "Abv[hۑtH_IĂ",
                UseDescriptionForTitle = true,
                SelectedPath = Directory.Exists(_config.StorageDir) ? _config.StorageDir : AppContext.BaseDirectory
            };

            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            var selected = dlg.SelectedPath;
            Directory.CreateDirectory(selected);

            _config.StorageDir = selected;
            SaveConfig();
            ApplyConfigToOptionalUi();
        }

        private T? FindControl<T>(string name) where T : Control
        {
            foreach (var c in GetAllControls(this))
            {
                if (c is T t && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                    return t;
            }
            return null;
        }

        private static IEnumerable<Control> GetAllControls(Control root)
        {
            foreach (Control c in root.Controls)
            {
                yield return c;
                foreach (var cc in GetAllControls(c))
                    yield return cc;
            }
        }

        // DesignerCxg
        private void Clear_Click(object sender, EventArgs e) => Console.Clear();
        private void Quit_Click(object sender, EventArgs e) => Application.Exit();
        private void label1_Click(object sender, EventArgs e) { }

        private void TestPointCloudConvert()
        {
            //string projectId = "pc_test_001";
            //string webRoot = @"D:\VisualStudio\XRServer\bin\Debug\net8.0-windows\wwwroot";
            //string projectDir = Path.Combine(webRoot, "ANNOLINK", "AnnoLinkProject", projectId);
            //Directory.CreateDirectory(projectDir);

            //string sourcePath = @"D:\temp\sample.pts";
            //string ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            //string savedPath = Path.Combine(projectDir, "source" + ext);
            //File.Copy(sourcePath, savedPath, true);

            //var result = PointCloudConverter.ConvertFile(savedPath, projectDir);

            AppendConsole("point cloud converter test ");
        }
    }

    internal sealed class DwgConvertRequest
    {
        public string InputPath { get; set; } = "";
        public string OutputPath { get; set; } = "";
        public string? BgMode { get; set; }
    }

    public sealed class ServerConfig
    {
        public int Port { get; set; } = 1800;
        public string Url { get; set; } = "http://0.0.0.0:1800";

        public string StorageDir { get; set; } = "data";
        public List<string> AllowedCidrs { get; set; } = new() { "192.168.3.0/24" };
        public string WebRoot { get; set; } = "wwwroot";
        public string PhpRoot { get; set; } = "wwwroot";     //  WebRoot Ɠ
        public string PhpCgiPath { get; set; } = @"php\php-cgi.exe";

        /// <summary>
        /// 同梱 LibreOffice の soffice.exe パス。
        /// 空の場合は libreOffice\program\soffice.exe を自動検索。
        /// </summary>
        public string? LibreOfficePath { get; set; }

        public static ServerConfig CreateDefault() => new();
    }

    internal readonly struct CidrRule
    {
        private readonly uint _network;
        private readonly uint _mask;

        private CidrRule(uint network, uint mask)
        {
            _network = network;
            _mask = mask;
        }

        public static CidrRule LoopbackOnly()
        {
            return new CidrRule(ToUInt32(IPAddress.Loopback), 0xFFFFFFFFu); // 127.0.0.1/32
        }

        public bool Contains(IPAddress ip)
        {
            var v = ToUInt32(ip);
            return (v & _mask) == (_network & _mask);
        }

        public static bool TryParse(string cidr, out CidrRule rule)
        {
            rule = default;

            var parts = cidr.Split('/');
            if (parts.Length != 2) return false;

            if (!IPAddress.TryParse(parts[0], out var ip)) return false;
            if (ip.AddressFamily != AddressFamily.InterNetwork) return false;

            if (!int.TryParse(parts[1], out var prefix)) return false;
            if (prefix < 0 || prefix > 32) return false;

            var mask = prefix == 0 ? 0u : (0xFFFFFFFFu << (32 - prefix));
            var network = ToUInt32(ip) & mask;

            rule = new CidrRule(network, mask);
            return true;
        }

        private static uint ToUInt32(IPAddress ip)
        {
            var b = ip.GetAddressBytes();
            if (b.Length != 4) throw new ArgumentException("IPv4 only.");

            return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
        }

        
    }

}