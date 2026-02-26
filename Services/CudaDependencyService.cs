using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace EndFieldFightHelper.Services;

public record CudaDownloadProgress(string FileName, int FileIndex, int TotalFiles, double FileProgress);

public class CudaDependencyService
{
    private static readonly (string Url, string DisplayName)[] Packages =
    {
        ("https://developer.download.nvidia.com/compute/cuda/redist/cuda_cudart/windows-x86_64/cuda_cudart-windows-x86_64-12.6.77-archive.zip",
         "CUDA Runtime"),
        ("https://developer.download.nvidia.com/compute/cuda/redist/libcublas/windows-x86_64/libcublas-windows-x86_64-12.6.4.1-archive.zip",
         "cuBLAS"),
        ("https://developer.download.nvidia.com/compute/cuda/redist/libcufft/windows-x86_64/libcufft-windows-x86_64-11.3.0.4-archive.zip",
         "cuFFT"),
        ("https://developer.download.nvidia.com/compute/cuda/redist/libnvjitlink/windows-x86_64/libnvjitlink-windows-x86_64-12.6.77-archive.zip",
         "nvJitLink"),
        ("https://developer.download.nvidia.com/compute/cuda/redist/cuda_nvrtc/windows-x86_64/cuda_nvrtc-windows-x86_64-12.6.77-archive.zip",
         "NVRTC"),
        ("https://developer.download.nvidia.com/compute/cudnn/redist/cudnn/windows-x86_64/cudnn-windows-x86_64-9.6.0.74_cuda12-archive.zip",
         "cuDNN"),
    };

    private static readonly string[] RequiredDlls =
    {
        "cudart64_12.dll",
        "cublas64_12.dll",
        "cublasLt64_12.dll",
        "cufft64_11.dll",
        "cudnn64_9.dll",
        "cudnn_graph64_9.dll",
        "cudnn_ops64_9.dll",
        "cudnn_cnn64_9.dll",
        "cudnn_adv64_9.dll",
        "cudnn_heuristic64_9.dll",
        "cudnn_engines_runtime_compiled64_9.dll",
        "cudnn_engines_precompiled64_9.dll",
        "nvJitLink64_12.dll",
        "nvrtc-builtins64_120.dll",
    };

    private static readonly string[] CudaDllPatterns =
    {
        "cudart64_*.dll", "cublas64_*.dll", "cublasLt64_*.dll",
        "cufft64_*.dll",
        "nvblas64_*.dll", "cudnn*.dll", "nvrtc*.dll",
    };

    private static string GetInstallDir()
    {
        var ortProviderPath = FindOrtProviderDir();
        return ortProviderPath ?? AppContext.BaseDirectory;
    }

    private static string? FindOrtProviderDir()
    {
        var runtimesDir = Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native");
        var runtimesProviderPath = Path.Combine(runtimesDir, "onnxruntime_providers_cuda.dll");
        var rootProviderPath = Path.Combine(AppContext.BaseDirectory, "onnxruntime_providers_cuda.dll");
        var hasRuntimesProvider = File.Exists(runtimesProviderPath);
        var hasRootProvider = File.Exists(rootProviderPath);

        string? resolvedDir = null;
        if (hasRuntimesProvider)
            resolvedDir = runtimesDir;
        else if (hasRootProvider)
            resolvedDir = AppContext.BaseDirectory;

        return resolvedDir;
    }

    public static bool CheckInstalled()
    {
        MigrateFromRootIfNeeded();
        var dir = GetInstallDir();
        EnsureCompatibilityAliases(dir);
        var targetState = RequiredDlls.ToDictionary(d => d, d => File.Exists(Path.Combine(dir, d)));
        var installed = targetState.Values.All(v => v);

        return installed;
    }

    public static void MigrateFromRootIfNeeded()
    {
        var targetDir = FindOrtProviderDir();
        if (targetDir == null || targetDir == AppContext.BaseDirectory)
            return;

        foreach (var pattern in CudaDllPatterns)
        {
            foreach (var srcFile in Directory.GetFiles(AppContext.BaseDirectory, pattern))
            {
                var destFile = Path.Combine(targetDir, Path.GetFileName(srcFile));
                try
                {
                    if (!File.Exists(destFile))
                    {
                        File.Move(srcFile, destFile);
                    }
                    else
                    {
                        File.Delete(srcFile);
                    }
                }
                catch
                {
                }
            }
        }
    }

    public async Task DownloadAndInstallAsync(
        IProgress<CudaDownloadProgress> progress,
        CancellationToken ct)
    {
        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromMinutes(30);
        var tempDir = Path.Combine(Path.GetTempPath(), "EndFieldFightHelper_cuda");
        var installDir = GetInstallDir();

        try
        {
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(installDir);

            for (int i = 0; i < Packages.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                var (url, name) = Packages[i];
                var zipPath = Path.Combine(tempDir, Path.GetFileName(url));

                progress.Report(new CudaDownloadProgress(name, i + 1, Packages.Length, 0));
                await DownloadFileAsync(http, url, zipPath, (p) =>
                    progress.Report(new CudaDownloadProgress(name, i + 1, Packages.Length, p)), ct);

                ct.ThrowIfCancellationRequested();
                progress.Report(new CudaDownloadProgress($"{name} (解压中)", i + 1, Packages.Length, 100));

                _ = ExtractDllsFromZip(zipPath, installDir);
                EnsureCompatibilityAliases(installDir);

                try { File.Delete(zipPath); } catch { }
            }
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    private static async Task DownloadFileAsync(
        HttpClient http, string url, string destPath,
        Action<double> onProgress, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        long downloaded = 0;
        int read;

        while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
            downloaded += read;
            if (totalBytes > 0)
                onProgress((double)downloaded / totalBytes * 100);
        }
    }

    private static (int ExtractedCount, int LockedSkippedCount) ExtractDllsFromZip(string zipPath, string destDir)
    {
        using var archive = ZipFile.OpenRead(zipPath);

        var dllEntries = archive.Entries
            .Where(e => e.FullName.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
                        && e.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                        && e.Length > 0);

        var extractedCount = 0;
        var lockedSkippedCount = 0;
        foreach (var entry in dllEntries)
        {
            var destPath = Path.Combine(destDir, entry.Name);
            try
            {
                entry.ExtractToFile(destPath, overwrite: true);
                extractedCount++;
            }
            catch (IOException ex) when (IsSharingViolation(ex) && File.Exists(destPath))
            {
                lockedSkippedCount++;
            }
        }

        return (extractedCount, lockedSkippedCount);
    }

    private static bool IsSharingViolation(IOException ex)
    {
        const int ErrorSharingViolation = 32;
        const int ErrorLockViolation = 33;
        var win32Code = ex.HResult & 0xFFFF;
        return win32Code == ErrorSharingViolation || win32Code == ErrorLockViolation;
    }

    private static void EnsureCompatibilityAliases(string dir)
    {
        CreateAliasIfMissing(
            dir,
            "nvJitLink64_12.dll",
            new[] { "nvJitLink_*.dll", "nvJitLink64_*.dll" });

        CreateAliasIfMissing(
            dir,
            "nvrtc-builtins64_120.dll",
            new[] { "nvrtc-builtins64_*.dll" });
    }

    private static void CreateAliasIfMissing(
        string dir,
        string targetFileName,
        string[] sourcePatterns)
    {
        var targetPath = Path.Combine(dir, targetFileName);
        if (File.Exists(targetPath))
            return;

        foreach (var pattern in sourcePatterns)
        {
            var src = Directory.GetFiles(dir, pattern)
                .Where(p => !string.Equals(Path.GetFileName(p), targetFileName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (src == null)
                continue;

            try
            {
                File.Copy(src, targetPath, overwrite: false);
                return;
            }
            catch
            {
                // ignored - next candidate will be tried
            }
        }
    }

    public static void DeleteCudaDlls()
    {
        foreach (var dir in new[] { GetInstallDir(), AppContext.BaseDirectory })
        {
            foreach (var pattern in CudaDllPatterns)
            {
                foreach (var file in Directory.GetFiles(dir, pattern))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
    }
}
