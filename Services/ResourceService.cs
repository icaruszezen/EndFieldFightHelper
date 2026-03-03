using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using EndFieldFightHelper.Helpers;
using EndFieldFightHelper.Models;

namespace EndFieldFightHelper.Services;

public class ResourceService : IDisposable
{
    private const string CommitsApiUrl =
        "https://api.github.com/repos/Lieyuan621/Endaxis/commits?path=public&per_page=1";

    private const string ZipDownloadUrl =
        "https://github.com/Lieyuan621/Endaxis/archive/refs/heads/main.zip";

    private const string MetadataFileName = ".resource-meta.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
    };

    private readonly HttpClient _httpClient;

    public string? GitHubMirrorPrefix { get; set; }

    public string ResourceBasePath { get; } =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "public");

    private string MetadataPath => Path.Combine(ResourceBasePath, MetadataFileName);

    public ResourceService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("EndFieldFightHelper/1.0");
    }

    private string ApplyMirror(string url) =>
        GitHubMirrorHelper.ApplyMirror(url, GitHubMirrorPrefix);

    public bool CheckResourcesExist()
    {
        var gamedataPath = Path.Combine(ResourceBasePath, "gamedata.json");
        return File.Exists(gamedataPath);
    }

    public ResourceMetadata? LoadMetadata()
    {
        if (!File.Exists(MetadataPath))
            return null;

        try
        {
            var json = File.ReadAllText(MetadataPath);
            return JsonSerializer.Deserialize<ResourceMetadata>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private void SaveMetadata(ResourceMetadata metadata)
    {
        var dir = Path.GetDirectoryName(MetadataPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(metadata, JsonOptions);
        File.WriteAllText(MetadataPath, json);
    }

    public async Task<(bool HasUpdate, string LatestSha, string Message)> CheckForUpdateAsync(
        CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(ApplyMirror(CommitsApiUrl), ct);
            response.EnsureSuccessStatusCode();

            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

            var commits = doc.RootElement;
            if (commits.GetArrayLength() == 0)
                return (false, "", "检查更新失败: 未找到远程提交记录");

            var latestSha = commits[0].GetProperty("sha").GetString() ?? "";
            var commitMessage = commits[0]
                .GetProperty("commit")
                .GetProperty("message")
                .GetString() ?? "";

            var local = LoadMetadata();
            if (local == null || string.IsNullOrEmpty(local.CommitSha))
                return (true, latestSha, commitMessage);

            var hasUpdate = !string.Equals(local.CommitSha, latestSha, StringComparison.OrdinalIgnoreCase);
            return (hasUpdate, latestSha, commitMessage);
        }
        catch (Exception ex)
        {
            return (false, "", $"检查更新失败: {ex.Message}");
        }
    }

    public async Task DownloadResourcesAsync(
        IProgress<(string Status, double Percent)>? progress = null,
        CancellationToken ct = default)
    {
        var tempDir = Path.Combine(Path.GetTempPath(),
            $"endaxis-dl-{Guid.NewGuid():N}");
        var zipPath = Path.Combine(tempDir, "repo.zip");

        try
        {
            Directory.CreateDirectory(tempDir);

            progress?.Report(("正在获取版本信息...", 0));
            var (_, latestSha, _) = await CheckForUpdateAsync(ct);

            progress?.Report(("正在下载资源包...", 5));
            using (var response = await _httpClient.GetAsync(ApplyMirror(ZipDownloadUrl),
                       HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? -1;

                await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
                await using var fileStream = new FileStream(zipPath, FileMode.Create,
                    FileAccess.Write, FileShare.None, 81920, true);

                var buffer = new byte[81920];
                long downloadedBytes = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    downloadedBytes += bytesRead;

                    if (totalBytes > 0)
                    {
                        var pct = 5 + downloadedBytes * 70.0 / totalBytes;
                        var sizeMb = downloadedBytes / 1048576.0;
                        var totalMb = totalBytes / 1048576.0;
                        progress?.Report(($"正在下载... {sizeMb:F1}/{totalMb:F1} MB", Math.Min(pct, 75)));
                    }
                    else
                    {
                        var sizeMb = downloadedBytes / 1048576.0;
                        progress?.Report(($"正在下载... {sizeMb:F1} MB", 40));
                    }
                }
            }

            ct.ThrowIfCancellationRequested();

            progress?.Report(("正在解压资源...", 78));
            var extractDir = Path.Combine(tempDir, "extracted");
            ZipFile.ExtractToDirectory(zipPath, extractDir);

            var sourcePath = Directory.GetDirectories(extractDir)
                .Select(d => Path.Combine(d, "public"))
                .FirstOrDefault(Directory.Exists);

            if (sourcePath == null)
                throw new InvalidOperationException("下载的压缩包中未找到 public/ 目录");

            progress?.Report(("正在同步文件...", 85));

            if (Directory.Exists(ResourceBasePath))
                Directory.Delete(ResourceBasePath, true);

            Directory.CreateDirectory(ResourceBasePath);

            CopyDirectory(sourcePath, ResourceBasePath);

            progress?.Report(("正在保存版本信息...", 95));
            SaveMetadata(new ResourceMetadata
            {
                CommitSha = string.IsNullOrEmpty(latestSha) ? "unknown" : latestSha,
                LastUpdated = DateTime.UtcNow
            });

            progress?.Report(("资源下载完成", 100));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); }
                catch { /* best effort cleanup */ }
            }
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
            CopyDirectory(subDir, destSubDir);
        }
    }
}
