namespace BV6Tools.Services.ManifestDownloader.Models;

public readonly record struct ManifestURL(string Repo, string SHA, string Path);

public class ManifestUrl
{
    public string Repo { get; set; } = string.Empty;
    public string SHA { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;

    public override bool Equals(object? obj)
    {
        if (obj is ManifestUrl other) return Repo == other.Repo && SHA == other.SHA && Path == other.Path;
        return false;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Repo, SHA, Path);
    }
}