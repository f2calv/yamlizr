namespace CasCap.Utilities;

/// <summary>Matches Azure DevOps definition folder paths, which always use '\' regardless of the host OS.</summary>
public static class FolderMatcher
{
    /// <summary>True when <paramref name="path"/> equals <paramref name="folder"/> or is beneath it (case-insensitive, '/' or '\' accepted).</summary>
    public static bool Matches(string? path, string? folder)
    {
        var p = Normalise(path);
        var f = Normalise(folder);
        return p.Equals(f, StringComparison.OrdinalIgnoreCase)
            || p.StartsWith(f.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalise(string? s)
        => "\\" + (s ?? string.Empty).Replace('/', '\\').Trim('\\');
}
