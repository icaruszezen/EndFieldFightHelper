using System;

namespace EndFieldFightHelper.Models;

public class AppUpdateInfo
{
    public string Version { get; set; } = "";
    public string TagName { get; set; } = "";
    public string ReleaseName { get; set; } = "";
    public string ReleaseNotes { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public DateTime PublishedAt { get; set; }
    public long FileSize { get; set; }
}
