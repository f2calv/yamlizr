using CasCap.Common.Extensions;

namespace CasCap.Utilities;

/// <summary>Matches Azure DevOps definition folder paths, which always use '\' regardless of the host OS.</summary>
public static class FolderMatcher
{
    private static readonly char[] _separators = ['\\', '/'];

    /// <summary>True when <paramref name="path"/> equals <paramref name="folder"/> or is beneath it (case-insensitive, '/' or '\' accepted).</summary>
    public static bool Matches(string? path, string? folder)
    {
        var p = Normalise(path);
        var f = Normalise(folder);
        return p.Equals(f, StringComparison.OrdinalIgnoreCase)
            || p.StartsWith(f.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Splits an Azure DevOps folder path (either separator) into sanitized, file-system-safe segments.</summary>
    public static string[] GetSubFolders(string? folder)
        => [.. (folder ?? string.Empty).Split(_separators, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Sanitize()).OfType<string>()];

    private static string Normalise(string? s)
        => "\\" + (s ?? string.Empty).Replace('/', '\\').Trim('\\');
}
