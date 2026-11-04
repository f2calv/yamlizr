using CasCap.Utilities;

namespace CasCap.Api.AzureDevOps.Tests.Unit;

/// <summary>Tests for the <c>--folder</c> definition path matching.</summary>
[Trait("Category", "Folder")]
public class FolderMatcherTests
{
    [Theory]
    [InlineData(@"\Team\App1", @"\Team\App1")]
    [InlineData(@"\Team\App1\Sub", @"\Team\App1")]
    [InlineData(@"\Team\App1\Sub\Deep", @"\Team\App1")]
    [InlineData(@"\team\APP1", @"\Team\App1")]
    [InlineData(@"\Team\App1", @"Team\App1")]
    [InlineData(@"\Team\App1", @"\Team\App1\")]
    [InlineData(@"\Team\App1", "/Team/App1")]
    [InlineData(@"\Team\App1", @"\")]
    [InlineData(@"\", @"\")]
    [InlineData(null, @"\")]
    public void Matches_PathInsideFolder_ReturnsTrue(string? path, string folder)
        => Assert.True(FolderMatcher.Matches(path, folder));

    [Theory]
    [InlineData(@"\Team\App2", @"\Team\App1")]
    [InlineData(@"\Team", @"\Team\App1")]
    [InlineData(@"\Other\App1", @"\Team\App1")]
    [InlineData(@"\", @"\Team")]
    [InlineData(null, @"\Team")]
    public void Matches_PathOutsideFolder_ReturnsFalse(string? path, string folder)
        => Assert.False(FolderMatcher.Matches(path, folder));
}
