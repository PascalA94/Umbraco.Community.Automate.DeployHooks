namespace Umbraco.Community.Automate.DeployHooks.Http;

/// <summary>
/// Last line of defence for a message that is about to leave the package: removes the credential values the sender
/// held, and for a URL also its host, path and its last two path segments (the project and hook ids) when they are long enough to be an id rather than a word like "deploy". Every message is already built from fixed text,
/// so this only matters if a later change interpolates something it should not.
/// </summary>
internal static class Scrubber
{
    private const string Redacted = "[redacted]";
    private const int MinSegmentLength = 8;

    public static string Scrub(string text, params string?[] sensitive)
    {
        foreach (var value in sensitive.Where(v => !string.IsNullOrWhiteSpace(v)).SelectMany(Expand).Distinct(StringComparer.Ordinal).OrderByDescending(v => v.Length))
        {
            text = text.Replace(value, Redacted, StringComparison.OrdinalIgnoreCase);
        }

        return text;
    }

    private static IEnumerable<string> Expand(string? value)
    {
        yield return value!;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
        {
            yield break;
        }

        yield return uri.Host;
        if (uri.AbsolutePath.Length > 1)
        {
            yield return uri.AbsolutePath;
        }

        foreach (var segment in uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).TakeLast(2).Where(s => s.Length >= MinSegmentLength))
        {
            yield return segment;
        }
    }
}
