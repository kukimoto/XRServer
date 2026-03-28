using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace XRServer
{
    internal static class PhpCgiHandler
    {
        /// <summary>
        /// PHP-CGI で /p/*.php を実行するハンドラ
        /// - documentRoot: phproot の物理パス
        /// - urlPrefix: "/p"（URL上のプレフィクス）
        /// </summary>
        public static async Task HandleAsync(
            HttpContext ctx,
            string phpCgiPath,
            string documentRoot,
            string dataDir,
            AppSecrets? secrets,
            string urlPrefix,
            Action<string> log,
            Action<string> logWarn,
            Action<string, Exception> logError)
        {
            // 対象が .php 以外なら 404
            var reqPath = ctx.Request.Path.Value ?? "/";
            if (!reqPath.EndsWith(".php", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Response.StatusCode = 404;
                await ctx.Response.WriteAsync("Not Found");
                return;
            }

            // URL: /p/xxx.php -> documentRoot\xxx.php
            var rel = reqPath.StartsWith(urlPrefix + "/", StringComparison.OrdinalIgnoreCase)
                ? reqPath.Substring((urlPrefix + "/").Length)
                : reqPath.TrimStart('/');

            // パストラバーサル対策
            rel = rel.Replace('\\', '/');
            if (rel.Contains(".."))
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Bad Request");
                return;
            }

            var scriptFile = Path.GetFullPath(Path.Combine(documentRoot, rel));
            var rootFull = Path.GetFullPath(documentRoot);

            if (!scriptFile.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) || !File.Exists(scriptFile))
            {
                ctx.Response.StatusCode = 404;
                await ctx.Response.WriteAsync("Not Found");
                return;
            }

            Directory.CreateDirectory(dataDir);

            // QUERY_STRING
            var queryString = ctx.Request.QueryString.HasValue ? ctx.Request.QueryString.Value!.TrimStart('?') : "";

            if (!File.Exists(phpCgiPath))
            {
                ctx.Response.StatusCode = 500;
                await ctx.Response.WriteAsync("php-cgi.exe not found");
                logWarn($"php-cgi.exe not found: {phpCgiPath}");
                return;
            }

            // php-cgi.exe があるディレクトリ（php.ini / dll / ext / cacert.pem もここに置く前提）
            var phpDir = Path.GetDirectoryName(phpCgiPath) ?? "";

            // 同梱CAバンドル（ここに置く）
            var caFile = Path.Combine(phpDir, "cacert.pem");
            var caFileForIni = caFile.Replace('\\', '/'); // INI向けは / の方が事故りにくい

            // php.ini
            var phpIni = Path.Combine(phpDir, "php.ini");
            if (!File.Exists(phpIni))
                logWarn($"php.ini not found: {phpIni} (still try run)");

            var psi = new ProcessStartInfo
            {
                FileName = phpCgiPath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,

                // ★これが効く（php8.dll / ext / php.ini を拾いやすい）
                WorkingDirectory = phpDir
            };

            // PHP設定：php.ini を読ませつつ、必要な値はコマンドラインで上書き
            // - REDIRECT_STATUS は必須（force-cgi-redirect対策）
            // - curl/openssl の CA は -d で強制（これが一番確実）
            var args = new StringBuilder();
            args.Append($"-c \"{phpIni}\" ");

            // ★CAファイルがある場合は、確実にPHPへ「これを使え」と明示
            if (File.Exists(caFile))
            {
                // curl / openssl の両方に指定（冗長だが安定）
                args.Append($"-d curl.cainfo=\"{caFileForIni}\" ");
                args.Append($"-d openssl.cafile=\"{caFileForIni}\" ");

                // 念のため環境変数もセット（効く環境もある）
                psi.Environment["CURL_CA_BUNDLE"] = caFile;
                psi.Environment["SSL_CERT_FILE"] = caFile;
            }
            else
            {
                // ここが出ているなら、まず cacert.pem が置けていない
                logWarn($"cacert.pem not found: {caFile}  → curl error 60 の原因になり得ます");
            }

            args.Append($"-q -f \"{scriptFile}\"");
            psi.Arguments = args.ToString();

            // php.ini 等を確実に拾う
            psi.Environment["PHPRC"] = phpDir;

            // CGI必須
            psi.Environment["REDIRECT_STATUS"] = "200";
            psi.Environment["GATEWAY_INTERFACE"] = "CGI/1.1";
            psi.Environment["SERVER_PROTOCOL"] = ctx.Request.Protocol;
            psi.Environment["SERVER_SOFTWARE"] = "XRServer";
            psi.Environment["REQUEST_METHOD"] = ctx.Request.Method;
            psi.Environment["SCRIPT_FILENAME"] = scriptFile;
            psi.Environment["SCRIPT_NAME"] = reqPath;
            psi.Environment["DOCUMENT_ROOT"] = rootFull;
            psi.Environment["QUERY_STRING"] = queryString;
            psi.Environment["REMOTE_ADDR"] = ctx.Connection.RemoteIpAddress?.ToString() ?? "0.0.0.0";
            psi.Environment["SERVER_NAME"] = ctx.Request.Host.Host;
            psi.Environment["SERVER_PORT"] = (ctx.Request.Host.Port ?? 80).ToString();
            psi.Environment["REQUEST_URI"] = reqPath + (ctx.Request.QueryString.HasValue ? ctx.Request.QueryString.Value : "");

            // PATH：phpDir を先頭に（php8.dll / libcrypto / libssl 等の探索）
            var oldPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            psi.Environment["PATH"] = phpDir + ";" + oldPath;

            // PHP側の保存先（ユーザ指定StorageDir）
            psi.Environment["XR_DATA_DIR"] = dataDir;
            psi.Environment["TEMP"] = dataDir;
            psi.Environment["TMP"] = dataDir;

            // Gemini用の環境変数を php-cgi.exe プロセスにだけ注入
            if (secrets != null)
            {
                if (!string.IsNullOrWhiteSpace(secrets.GEMINI_API_KEY))
                    psi.Environment["GEMINI_API_KEY"] = secrets.GEMINI_API_KEY;

                if (!string.IsNullOrWhiteSpace(secrets.GEMINI_MODEL))
                    psi.Environment["GEMINI_MODEL"] = secrets.GEMINI_MODEL;

                if (!string.IsNullOrWhiteSpace(secrets.APP_ORIGIN))
                    psi.Environment["APP_ORIGIN"] = secrets.APP_ORIGIN;
            }

            // ヘッダをHTTP_XXXで渡す
            foreach (var h in ctx.Request.Headers)
            {
                var key = "HTTP_" + h.Key.ToUpperInvariant().Replace('-', '_');
                psi.Environment[key] = h.Value.ToString();
            }

            // POSTの場合は body を stdin に流す
            if (HttpMethods.IsPost(ctx.Request.Method))
            {
                psi.Environment["CONTENT_TYPE"] = ctx.Request.ContentType ?? "";
                psi.Environment["CONTENT_LENGTH"] = (ctx.Request.ContentLength ?? 0).ToString();
            }

            using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };

            try
            {
                p.Start();

                // stdin（POST body）
                if (HttpMethods.IsPost(ctx.Request.Method))
                {
                    await ctx.Request.Body.CopyToAsync(p.StandardInput.BaseStream);
                }
                p.StandardInput.Close();

                // stdout/stderr 同時取得
                var stdoutTask = ReadAllBytesAsync(p.StandardOutput.BaseStream);
                var stderrTask = p.StandardError.ReadToEndAsync();

                // タイムアウト（ハング対策）
                var exited = await Task.Run(() => p.WaitForExit(30_000)); // 30秒
                if (!exited)
                {
                    try { p.Kill(entireProcessTree: true); } catch { }
                    ctx.Response.StatusCode = 504;
                    await ctx.Response.WriteAsync("PHP timeout");
                    logWarn($"PHP timeout: {reqPath}");
                    return;
                }

                var stdoutBytes = await stdoutTask;
                var stderr = await stderrTask;

                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    logWarn($"PHP stderr ({reqPath}): {stderr.Trim()}");
                }

                var (headers, bodyBytes) = SplitCgiResponse(stdoutBytes);

                // ヘッダ反映（Status: "302 Found" など）
                static string SanitizeHeaderValue(string v)
                {
                    if (string.IsNullOrEmpty(v)) return "";
                    v = v.Replace("\r", "").Replace("\n", "").Trim();
                    var sb = new StringBuilder(v.Length);
                    foreach (var ch in v)
                    {
                        if (ch < 0x20 || ch == 0x7F) continue;
                        sb.Append(ch);
                    }
                    return sb.ToString();
                }

                static bool IsValidHeaderName(string name)
                {
                    if (string.IsNullOrEmpty(name)) return false;
                    foreach (var ch in name)
                    {
                        if (ch <= 0x20 || ch >= 0x7F) return false;
                        if (ch == ':' || ch == '<' || ch == '>' || ch == '"' || ch == '\\') return false;
                    }
                    return true;
                }

                // Status
                if (headers.TryGetValue("Status", out var statusLines) && statusLines.Count > 0)
                {
                    var sp = statusLines[0].Split(' ', 2);
                    if (int.TryParse(sp[0], out var code)) ctx.Response.StatusCode = code;
                }

                foreach (var kv in headers)
                {
                    if (kv.Key.Equals("Status", StringComparison.OrdinalIgnoreCase)) continue;

                    if (!IsValidHeaderName(kv.Key))
                    {
                        logWarn($"Skip invalid header name: [{kv.Key}]");
                        continue;
                    }

                    foreach (var rawVal in kv.Value)
                    {
                        var val = SanitizeHeaderValue(rawVal);
                        if (val.Length == 0) continue;

                        if (kv.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
                            ctx.Response.Headers.Append(kv.Key, val);
                        else
                            ctx.Response.Headers[kv.Key] = val;
                    }
                }

                // Content-Typeが無い場合の保険
                if (!ctx.Response.Headers.ContainsKey("Content-Type"))
                    ctx.Response.ContentType = "text/html; charset=utf-8";

                await ctx.Response.Body.WriteAsync(bodyBytes, 0, bodyBytes.Length);

                log($"PHP OK: {reqPath} exit={p.ExitCode}");
            }
            catch (Exception ex)
            {
                logError($"PHP failed: {reqPath}", ex);
                if (!ctx.Response.HasStarted)
                {
                    ctx.Response.StatusCode = 500;
                    await ctx.Response.WriteAsync("PHP error");
                }
            }
        }

        private static async Task<byte[]> ReadAllBytesAsync(Stream s)
        {
            using var ms = new MemoryStream();
            await s.CopyToAsync(ms);
            return ms.ToArray();
        }

        private static (Dictionary<string, List<string>> headers, byte[] body) SplitCgiResponse(byte[] stdout)
        {
            static bool IsLb(byte b) => b == (byte)'\r' || b == (byte)'\n';

            int bodyStart = -1;

            for (int i = 0; i < stdout.Length; i++)
            {
                if (!IsLb(stdout[i])) continue;

                int j = i;
                if (stdout[j] == (byte)'\r' && j + 1 < stdout.Length && stdout[j + 1] == (byte)'\n') j += 2;
                else j += 1;

                if (j >= stdout.Length) break;

                if (IsLb(stdout[j]))
                {
                    if (stdout[j] == (byte)'\r' && j + 1 < stdout.Length && stdout[j + 1] == (byte)'\n') bodyStart = j + 2;
                    else bodyStart = j + 1;
                    break;
                }
            }

            if (bodyStart < 0)
                return (new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase), stdout);

            var headerBytes = stdout[..(bodyStart)];
            var body = stdout[bodyStart..];

            var headerText = Encoding.ASCII.GetString(headerBytes);
            headerText = headerText.Replace("\r\n", "\n").Replace("\r", "\n");

            var headers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var rawLine in headerText.Split('\n'))
            {
                var line = rawLine.TrimEnd();
                if (line.Length == 0) continue;

                int p = line.IndexOf(':');
                if (p <= 0) continue;

                var k = line[..p].Trim();
                var v = line[(p + 1)..].Trim();

                if (!headers.TryGetValue(k, out var list))
                {
                    list = new List<string>();
                    headers[k] = list;
                }
                list.Add(v);
            }

            return (headers, body);
        }
    }
}