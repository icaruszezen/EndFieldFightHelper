using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using EndFieldFightHelper.Helpers;
using EndFieldFightHelper.Models;

namespace EndFieldFightHelper.Services;

public class AppUpdateService : IDisposable
{
    private const string ReleasesApiUrl =
        "https://api.github.com/repos/icaruszezen/EndFieldFightHelper/releases?per_page=1";

    private const string ExpectedAssetName = "EndFieldFightHelper-win-x64.zip";

    private readonly HttpClient _httpClient;
    private string? _pendingUpdateDir;

    public string? GitHubMirrorPrefix { get; set; }

    public AppUpdateService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("EndFieldFightHelper/1.0");
    }

    public static string GetCurrentVersion()
    {
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        return ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "0.0.0";
    }

    public async Task<(bool HasUpdate, AppUpdateInfo? Info, string Message)> CheckForUpdateAsync(
        CancellationToken ct = default)
    {
        try
        {
            using var response = await GitHubMirrorHelper.GetWithFallbackAsync(
                _httpClient, ReleasesApiUrl, GitHubMirrorPrefix,
                HttpCompletionOption.ResponseContentRead, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return (false, null, "暂无发布版本（仓库不存在、无 Release 或仓库为私有）");

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                return (false, null, "API 请求被拒绝（可能是请求频率超限，请稍后再试）");

            response.EnsureSuccessStatusCode();

            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                if (root.GetArrayLength() == 0)
                    return (false, null, "暂无发布版本");
                return ParseRelease(root[0]);
            }

            return ParseRelease(root);
        }
        catch (HttpRequestException ex)
        {
            return (false, null, $"检查更新失败: 网络错误 ({ex.Message})");
        }
        catch (Exception ex)
        {
            return (false, null, $"检查更新失败: {ex.Message}");
        }
    }

    private (bool HasUpdate, AppUpdateInfo? Info, string Message) ParseRelease(JsonElement root)
    {
        if (!root.TryGetProperty("tag_name", out var tagNameProp))
            return (false, null, "检查更新失败: 响应格式异常（缺少 tag_name 字段）");

        var tagName = tagNameProp.GetString() ?? "";
        var remoteVersion = tagName.TrimStart('v', 'V');

        if (!Version.TryParse(remoteVersion, out var remote))
            return (false, null, $"无法解析远程版本号: {tagName}");

        var currentVersion = GetCurrentVersion();
        if (!Version.TryParse(currentVersion, out var local))
            return (false, null, $"无法解析本地版本号: {currentVersion}");

        if (remote <= local)
            return (false, null, "已是最新版本");

        string downloadUrl = "";
        long fileSize = 0;
        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                if (!asset.TryGetProperty("name", out var nameProp))
                    continue;
                var name = nameProp.GetString() ?? "";
                if (string.Equals(name, ExpectedAssetName, StringComparison.OrdinalIgnoreCase))
                {
                    downloadUrl = asset.TryGetProperty("browser_download_url", out var urlProp)
                        ? urlProp.GetString() ?? ""
                        : "";
                    fileSize = asset.TryGetProperty("size", out var sizeProp)
                        && sizeProp.TryGetInt64(out var sizeVal)
                            ? sizeVal
                            : 0;
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(downloadUrl))
            return (false, null, "新版本中未找到可用的下载文件");

        var releaseName = root.TryGetProperty("name", out var rnProp)
            ? rnProp.GetString() ?? tagName
            : tagName;
        var releaseNotes = root.TryGetProperty("body", out var bodyProp)
            ? bodyProp.GetString() ?? ""
            : "";

        var info = new AppUpdateInfo
        {
            Version = remoteVersion,
            TagName = tagName,
            ReleaseName = releaseName,
            ReleaseNotes = releaseNotes,
            DownloadUrl = downloadUrl,
            PublishedAt = root.TryGetProperty("published_at", out var pub)
                         && DateTimeOffset.TryParse(pub.GetString(), CultureInfo.InvariantCulture,
                             DateTimeStyles.None, out var dto)
                ? dto.DateTime
                : DateTime.MinValue,
            FileSize = fileSize
        };

        return (true, info, $"发现新版本 {remoteVersion}");
    }

    public async Task DownloadUpdateAsync(
        AppUpdateInfo updateInfo,
        IProgress<(string Status, double Percent)>? progress = null,
        CancellationToken ct = default)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"effh-update-{Guid.NewGuid():N}");
        var zipPath = Path.Combine(tempDir, ExpectedAssetName);

        try
        {
            Directory.CreateDirectory(tempDir);

            progress?.Report(("正在下载更新包...", 2));
            using (var response = await GitHubMirrorHelper.GetWithFallbackAsync(
                       _httpClient, updateInfo.DownloadUrl, GitHubMirrorPrefix,
                       HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? updateInfo.FileSize;

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
                        var pct = 2 + downloadedBytes * 73.0 / totalBytes;
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

            progress?.Report(("正在解压更新...", 78));
            var extractDir = Path.Combine(tempDir, "extracted");
            ZipFile.ExtractToDirectory(zipPath, extractDir);

            File.Delete(zipPath);

            _pendingUpdateDir = extractDir;
            progress?.Report(("更新已准备就绪，即将重启...", 100));
        }
        catch
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); }
                catch { /* best effort */ }
            }
            throw;
        }
    }

    private static string EscapeBatPath(string path) => path.Replace("%", "%%");

    public void ApplyUpdateAndRestart()
    {
        if (string.IsNullOrEmpty(_pendingUpdateDir) || !Directory.Exists(_pendingUpdateDir))
            throw new InvalidOperationException("没有待安装的更新");

        var appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
        var exeName = Path.GetFileName(Environment.ProcessPath ?? "EndFieldFightHelper.exe");
        var pid = Environment.ProcessId;
        var tempDir = Path.GetDirectoryName(_pendingUpdateDir)!;
        var scriptPath = Path.Combine(tempDir, "update.bat");

        var escapedUpdateDir = EscapeBatPath(_pendingUpdateDir);
        var escapedAppDir = EscapeBatPath(appDir);
        var escapedExePath = EscapeBatPath(Path.Combine(appDir, exeName));
        var escapedTempDir = EscapeBatPath(tempDir);

        var script = new StringBuilder();
        script.AppendLine("@echo off");
        script.AppendLine("chcp 65001 >nul 2>&1");
        script.AppendLine($"echo 正在等待 EndFieldFightHelper (PID {pid}) 退出...");
        script.AppendLine(":wait");
        script.AppendLine($"tasklist /FI \"PID eq {pid}\" 2>NUL | find /I \"{pid}\" >NUL");
        script.AppendLine("if not errorlevel 1 (");
        script.AppendLine("    timeout /t 1 /nobreak >nul");
        script.AppendLine("    goto wait");
        script.AppendLine(")");
        script.AppendLine("echo 正在安装更新...");
        script.AppendLine($"xcopy \"{escapedUpdateDir}\\*\" \"{escapedAppDir}\\\" /E /Y /Q >nul 2>&1");
        script.AppendLine("if errorlevel 1 (");
        script.AppendLine("    echo 更新失败，请手动解压更新包。");
        script.AppendLine("    pause");
        script.AppendLine("    exit /b 1");
        script.AppendLine(")");
        script.AppendLine("echo 更新完成，正在重启...");
        script.AppendLine($"start \"\" \"{escapedExePath}\"");
        script.AppendLine("cd /d \"%TEMP%\"");
        script.AppendLine($"rd /s /q \"{escapedTempDir}\" >nul 2>&1");

        File.WriteAllText(scriptPath, script.ToString(), new UTF8Encoding(false));

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{scriptPath}\"",
            UseShellExecute = true,
            CreateNoWindow = false,
            WindowStyle = ProcessWindowStyle.Minimized
        });

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
            lifetime.Shutdown(0);
        else
            Environment.Exit(0);
    }

    public void CleanupPendingUpdate()
    {
        if (string.IsNullOrEmpty(_pendingUpdateDir))
            return;

        var tempDir = Path.GetDirectoryName(_pendingUpdateDir);
        if (!string.IsNullOrEmpty(tempDir) && Directory.Exists(tempDir))
        {
            try { Directory.Delete(tempDir, true); }
            catch { /* best effort */ }
        }
        _pendingUpdateDir = null;
    }

    public void Dispose()
    {
        CleanupPendingUpdate();
        _httpClient.Dispose();
    }
}
