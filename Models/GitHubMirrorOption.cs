namespace EndFieldFightHelper.Models;

public record GitHubMirrorOption(string Name, string Url, bool IsCustom = false)
{
    public override string ToString() => Name;
}
