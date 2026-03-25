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
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("EndFieldFightHelper/1.0");
    }

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
            using var response = await GitHubMirrorHelper.GetWithFallbackAsync(
                _httpClient, CommitsApiUrl, GitHubMirrorPrefix,
                HttpCompletionOption.ResponseContentRead, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                return (false, "", "API 请求被拒绝（可能是请求频率超限，请稍后再试）");

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return (false, "", "检查更新失败: 远程仓库不存在或无法访问");

            response.EnsureSuccessStatusCode();

            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

            var commits = doc.RootElement;
            if (commits.ValueKind != JsonValueKind.Array || commits.GetArrayLength() == 0)
                return (false, "", "检查更新失败: 未找到远程提交记录");

            var firstCommit = commits[0];
            if (!firstCommit.TryGetProperty("sha", out var shaProp))
                return (false, "", "检查更新失败: 响应格式异常（缺少 sha 字段）");

            var latestSha = shaProp.GetString() ?? "";

            var commitMessage = "";
            if (firstCommit.TryGetProperty("commit", out var commitObj)
                && commitObj.TryGetProperty("message", out var msgProp))
            {
                commitMessage = msgProp.GetString() ?? "";
            }

            var local = LoadMetadata();
            if (local == null || string.IsNullOrEmpty(local.CommitSha))
                return (true, latestSha, commitMessage);

            var hasUpdate = !string.Equals(local.CommitSha, latestSha, StringComparison.OrdinalIgnoreCase);
            return (hasUpdate, latestSha, commitMessage);
        }
        catch (HttpRequestException ex)
        {
            return (false, "", $"检查更新失败: 网络错误 ({ex.Message})");
        }
        catch (JsonException ex)
        {
            return (false, "", $"检查更新失败: 响应解析错误 ({ex.Message})");
        }
        catch (Exception ex)
        {
            return (false, "", $"检查更新失败: {ex.Message}");
        }
    }

    public async Task DownloadResourcesAsync(
        string? commitSha = null,
        IProgress<(string Status, double Percent)>? progress = null,
        CancellationToken ct = default)
    {
        var tempDir = Path.Combine(Path.GetTempPath(),
            $"endaxis-dl-{Guid.NewGuid():N}");
        var zipPath = Path.Combine(tempDir, "repo.zip");

        try
        {
            Directory.CreateDirectory(tempDir);

            var latestSha = commitSha;
            if (string.IsNullOrEmpty(latestSha))
            {
                progress?.Report(("正在获取版本信息...", 0));
                (_, latestSha, _) = await CheckForUpdateAsync(ct);
            }

            progress?.Report(("正在下载资源包...", 5));
            using (var response = await GitHubMirrorHelper.GetWithFallbackAsync(
                       _httpClient, ZipDownloadUrl, GitHubMirrorPrefix,
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

            var stagingPath = ResourceBasePath + ".new";
            var backupPath = ResourceBasePath + ".old";

            if (Directory.Exists(stagingPath))
                Directory.Delete(stagingPath, true);
            if (Directory.Exists(backupPath))
                Directory.Delete(backupPath, true);

            Directory.CreateDirectory(stagingPath);
            CopyDirectory(sourcePath, stagingPath);

            if (Directory.Exists(ResourceBasePath))
                Directory.Move(ResourceBasePath, backupPath);

            try
            {
                Directory.Move(stagingPath, ResourceBasePath);
            }
            catch
            {
                if (!Directory.Exists(ResourceBasePath) && Directory.Exists(backupPath))
                    Directory.Move(backupPath, ResourceBasePath);
                throw;
            }

            if (Directory.Exists(backupPath))
            {
                try { Directory.Delete(backupPath, true); }
                catch { /* best effort */ }
            }

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
