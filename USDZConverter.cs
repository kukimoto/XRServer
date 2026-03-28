using Assimp;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace XRServer
{
    /// <summary>
    /// 設備リンク用の USDZ / GLB 変換補助。
    /// - USDZ は ZIP として展開する
    /// - 展開後の .usdc / .usd / .usda を AssimpNet で読み込み GLB に書き出す
    /// - state.usda の初期雛形を生成する
    /// - model.glb + state.usda + root.usda を USDZ として再パッケージする
    /// </summary>
    public static class USDZConverter
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private const string EmptyStateUsda = @"#usda 1.0
def Xform ""World"" {
    def Xform ""Annotations"" {
    }
}
";

        private const string RootUsda = @"#usda 1.0
(
    subLayers = [
        @./model.glb@,
        @./state.usda@
    ]
)
";

        public static void ToGlb(string usdzPath, string glbOutputPath)
        {
            if (string.IsNullOrWhiteSpace(usdzPath))
                throw new ArgumentException("USDZ path is empty.", nameof(usdzPath));
            if (string.IsNullOrWhiteSpace(glbOutputPath))
                throw new ArgumentException("GLB output path is empty.", nameof(glbOutputPath));
            if (!File.Exists(usdzPath))
                throw new FileNotFoundException("USDZ file not found.", usdzPath);

            var tempDir = Path.Combine(Path.GetTempPath(), "XRServer_USDZ_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                ZipFile.ExtractToDirectory(usdzPath, tempDir, overwriteFiles: true);

                var usdPath = Directory.EnumerateFiles(tempDir, "*", SearchOption.AllDirectories)
                    .Where(IsSupportedUsdFile)
                    .OrderBy(GetUsdPriority)
                    .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

                if (string.IsNullOrWhiteSpace(usdPath))
                    throw new InvalidOperationException("USDZ archive does not contain .usdc / .usd / .usda.");

                Directory.CreateDirectory(Path.GetDirectoryName(glbOutputPath) ?? throw new InvalidOperationException("Invalid GLB output directory."));

                using var ctx = new AssimpContext();
                var scene = ctx.ImportFile(usdPath, PostProcessSteps.Triangulate | PostProcessSteps.JoinIdenticalVertices | PostProcessSteps.SortByPrimitiveType);

                if (scene == null)
                    throw new InvalidOperationException("Assimp failed to import USD scene.");
                if (scene.MeshCount == 0 && scene.RootNode == null)
                    throw new InvalidOperationException("Imported USD scene is empty.");

                ctx.ExportFile(scene, glbOutputPath, "glb2");

                if (!File.Exists(glbOutputPath))
                    throw new IOException("GLB export failed. Output file was not created.");
            }
            catch (AssimpException ex)
            {
                throw new InvalidOperationException("USDZ → GLB conversion failed in Assimp. The Scaniverse USD/USDC may not be supported by the current AssimpNet build.", ex);
            }
            finally
            {
                TryDeleteDirectory(tempDir);
            }
        }

        public static void CreateEmptyStateUsda(string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("Output path is empty.", nameof(outputPath));

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? throw new InvalidOperationException("Invalid output directory."));
            File.WriteAllText(outputPath, EmptyStateUsda, Utf8NoBom);
        }

        public static void RepackToUsdz(string glbPath, string stateUsdaPath, string outputUsdzPath)
        {
            if (string.IsNullOrWhiteSpace(glbPath))
                throw new ArgumentException("GLB path is empty.", nameof(glbPath));
            if (string.IsNullOrWhiteSpace(stateUsdaPath))
                throw new ArgumentException("state.usda path is empty.", nameof(stateUsdaPath));
            if (string.IsNullOrWhiteSpace(outputUsdzPath))
                throw new ArgumentException("USDZ output path is empty.", nameof(outputUsdzPath));
            if (!File.Exists(glbPath))
                throw new FileNotFoundException("GLB file not found.", glbPath);
            if (!File.Exists(stateUsdaPath))
                throw new FileNotFoundException("state.usda file not found.", stateUsdaPath);

            var tempDir = Path.Combine(Path.GetTempPath(), "XRServer_USDZ_PACK_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                File.Copy(glbPath, Path.Combine(tempDir, "model.glb"), overwrite: true);
                File.Copy(stateUsdaPath, Path.Combine(tempDir, "state.usda"), overwrite: true);
                File.WriteAllText(Path.Combine(tempDir, "root.usda"), RootUsda, Utf8NoBom);

                Directory.CreateDirectory(Path.GetDirectoryName(outputUsdzPath) ?? throw new InvalidOperationException("Invalid USDZ output directory."));
                if (File.Exists(outputUsdzPath))
                    File.Delete(outputUsdzPath);

                // USDZ は「無圧縮ZIP」の方が互換性が高い
                ZipFile.CreateFromDirectory(tempDir, outputUsdzPath, CompressionLevel.NoCompression, includeBaseDirectory: false);
            }
            finally
            {
                TryDeleteDirectory(tempDir);
            }
        }

        private static bool IsSupportedUsdFile(string path)
        {
            var ext = Path.GetExtension(path)?.ToLowerInvariant();
            return ext == ".usdc" || ext == ".usd" || ext == ".usda";
        }

        private static int GetUsdPriority(string path)
        {
            return Path.GetExtension(path)?.ToLowerInvariant() switch
            {
                ".usdc" => 0,
                ".usd" => 1,
                ".usda" => 2,
                _ => 99
            };
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
    }
}
