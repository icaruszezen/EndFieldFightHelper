namespace EndFieldFightHelper.Helpers;

public static class GitHubMirrorHelper
{
    public static string ApplyMirror(string url, string? mirrorPrefix)
    {
        if (string.IsNullOrEmpty(mirrorPrefix))
            return url;
        return mirrorPrefix.TrimEnd('/') + "/" + url;
    }
}
