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
        return mirrorPrefix.TrimEnd('/') + "/" + url;
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
