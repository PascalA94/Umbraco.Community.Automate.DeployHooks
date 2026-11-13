using Umbraco.Community.Automate.DeployHooks.Actions;

namespace Umbraco.Community.Automate.DeployHooks.Tests;

public class PathCleanerTests
{
    public static TheoryData<string, string?> Paths() => new()
    {
        { "https://site.example/a?x=1#f", "/a" },
        { "http://site.example/news/a", "/news/a" },
        { "https://site.example", "/" },
        { "/a?x=1", "/a" },
        { "/a#frag", "/a" },
        { "/news/c", "/news/c" },
        { "  /padded  ", "/padded" },
        { "/%2Fx", "/%2Fx" },
        { "news/b", null },
        { "//evil.example/x", null },
        { "https://site.example//evil.example", null },
        { "\\\\evil.example\\x", null },
        { "/a\\b", null },
        { "/a\u0000b", null },
        { "/a\nb", null },
        { "/a\u007fb", null },
        { "javascript:alert(1)", null },
        { "ftp://site.example/a", null },
        { "?x=1", null },
        { "#top", null },
        { "/" + new string('a', 1023), "/" + new string('a', 1023) },
        { "/" + new string('a', 1024), null },
        { "/" + new string('a', 2000), null },
    };

    [Theory]
    [MemberData(nameof(Paths))]
    public void PathCleaner_Theory(string input, string? expected)
    {
        var ok = PathCleaner.TryCleanPath(input, out var cleaned);

        Assert.Equal(expected is not null, ok);
        Assert.Equal(expected, ok ? cleaned : null);
    }

    [Theory]
    [InlineData("news", true)]
    [InlineData("news:section/a-b_c.d", true)]
    [InlineData("UPPER123", true)]
    [InlineData("", false)]
    [InlineData("has space", false)]
    [InlineData("semi;colon", false)]
    [InlineData("new\nline", false)]
    [InlineData("a\u0000b", false)]
    [InlineData("é", false)]
    public void TagRule_Theory(string tag, bool valid)
    {
        Assert.Equal(valid, PathCleaner.IsValidTag(tag));
    }

    [Fact]
    public void TagRule_Length_256Ok_257Dropped()
    {
        Assert.True(PathCleaner.IsValidTag(new string('t', 256)));
        Assert.False(PathCleaner.IsValidTag(new string('t', 257)));
    }

    [Fact]
    public void Clean_SplitsLines_DedupesOrdinal_CountsDrops_SkipsBlanks()
    {
        var result = PathCleaner.CleanPaths("/a\r\n\n/b\n/a\nnews/x\n   \nhttps://s.example/b?q=1\n/A");

        Assert.Equal(["/a", "/b", "/A"], result.Values);
        Assert.Equal(1, result.Dropped);
    }

    [Fact]
    public void Clean_NullOrEmpty_IsEmpty()
    {
        Assert.Empty(PathCleaner.CleanPaths(null).Values);
        Assert.Empty(PathCleaner.CleanTags("").Values);
        Assert.Equal(0, PathCleaner.CleanTags("").Dropped);
    }
}
