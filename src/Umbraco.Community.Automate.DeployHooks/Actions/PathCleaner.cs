using System.Text.RegularExpressions;

namespace Umbraco.Community.Automate.DeployHooks.Actions;

/// <summary>
/// Cleans the paths and tags bound into a revalidate step (design 3.4). Content values are untrusted, so a bad value is
/// dropped and counted, never echoed and never an error: data must not be able to fail a step.
/// </summary>
internal static partial class PathCleaner
{
    public const int MaxPathLength = 1024;

    /// <summary>The values that passed, de-duplicated (ordinal, in first-seen order), and how many lines were dropped. Blank lines are neither.</summary>
    public readonly record struct Cleaned(IReadOnlyList<string> Values, int Dropped);

    // Next.js tag limit recalled as 256 characters (design 3.4, unverified).
    [GeneratedRegex(@"^[A-Za-z0-9:_\-./]{1,256}\z")]
    private static partial Regex TagPattern();

    public static Cleaned CleanPaths(string? lines) => Clean(lines, (string line, out string value) => TryCleanPath(line, out value));

    public static Cleaned CleanTags(string? lines) => Clean(lines, (string line, out string value) =>
    {
        value = line.Trim();
        return IsValidTag(value);
    });

    public static bool IsValidTag(string tag) => TagPattern().IsMatch(tag);

    /// <summary>
    /// An absolute http(s) URL is cut to its path; the query and fragment are dropped; the result must start with a single
    /// <c>/</c>. The path is otherwise kept as given (no decoding), because the site's own router decides what <c>%2F</c> means.
    /// </summary>
    public static bool TryCleanPath(string input, out string path)
    {
        path = string.Empty;
        var value = input.Trim();
        if (value.Any(c => char.IsControl(c) || c == '\\'))
        {
            return false;
        }

        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var afterScheme = value[(value.IndexOf("://", StringComparison.Ordinal) + 3)..];
            var slash = afterScheme.IndexOfAny(['/', '?', '#']);
            // No path means the site root.
            value = slash < 0 || afterScheme[slash] != '/' ? "/" : afterScheme[slash..];
        }

        var cut = value.IndexOfAny(['?', '#']);
        if (cut >= 0)
        {
            value = value[..cut];
        }

        // "//host/x" is a protocol-relative URL, not a path on this site.
        if (value.Length == 0 || value[0] != '/' || value.StartsWith("//", StringComparison.Ordinal) || value.Length > MaxPathLength)
        {
            return false;
        }

        path = value;
        return true;
    }

    private delegate bool TryClean(string line, out string value);

    private static Cleaned Clean(string? lines, TryClean tryClean)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var values = new List<string>();
        var dropped = 0;
        foreach (var line in (lines ?? string.Empty).Split(['\r', '\n']))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!tryClean(line, out var value))
            {
                dropped++;
            }
            else if (seen.Add(value))
            {
                values.Add(value);
            }
        }

        return new Cleaned(values, dropped);
    }
}
