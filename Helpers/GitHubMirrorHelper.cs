using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace EndFieldFightHelper.Helpers;

public static class GitHubMirrorHelper
{
    public static string ApplyMirror(string url, string? mirrorPrefix)
    {
        if (string.IsNullOrEmpty(mirrorPrefix))
            return url;

        var trimmed = mirrorPrefix.TrimEnd('/');
        if (!trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return url;

        return trimmed + "/" + url;
    }

    /// <summary>
    /// Returns true if the prefix is a valid HTTPS mirror URL (or empty/null meaning "no mirror").
    /// </summary>
    public static bool IsValidMirrorPrefix(string? mirrorPrefix)
    {
        if (string.IsNullOrWhiteSpace(mirrorPrefix))
            return true;
        return mirrorPrefix.TrimEnd('/').StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tries the mirrored URL first; on failure, falls back to the original URL.
    /// </summary>
    public static async Task<HttpResponseMessage> GetWithFallbackAsync(
        HttpClient client, string url, string? mirrorPrefix,
        HttpCompletionOption completionOption, CancellationToken ct)
    {
        var mirroredUrl = ApplyMirror(url, mirrorPrefix);

        if (mirroredUrl != url)
        {
            try
            {
                var response = await client.GetAsync(mirroredUrl, completionOption, ct);
                if (response.IsSuccessStatusCode)
                    return response;
                response.Dispose();
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { }
        }

        return await client.GetAsync(url, completionOption, ct);
    }
}
