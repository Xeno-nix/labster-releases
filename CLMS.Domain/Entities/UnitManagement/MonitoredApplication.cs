namespace CLMS.Domain.Entities.UnitManagement;

public enum ApplicationPlatform { Windows, MacOS, Both }

public sealed record MonitoredApplication(Guid Id, string Name, string ProcessName,
    ApplicationPlatform Platform, string? IconPath, bool IsEnabled, DateTime CreatedAt, DateTime UpdatedAt)
{
    public bool FlagForReview { get; init; }
}

public static class ApplicationProcessNames
{
    public static string Normalize(string? value)
    {
        var name = (value ?? "").Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4].TrimEnd();
        return name.ToLowerInvariant();
    }

    public static bool AppliesTo(ApplicationPlatform entry, ApplicationPlatform operatingSystem) =>
        operatingSystem != ApplicationPlatform.Both && (entry == ApplicationPlatform.Both || entry == operatingSystem);

    public const string DefaultIcon = "/images/application-default.svg";

    public static bool IsSafeIconPath(string? path)
    {
        if (path is null) return true;
        const string prefix = "/application-icons/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var file = path[prefix.Length..];
        var extension = Path.GetExtension(file);
        return (extension == ".png" || extension == ".webp")
            && Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out _)
            && file == Path.GetFileName(file);
    }
}
