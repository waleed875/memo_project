using MemoUpdateContracts;
using Xunit;

namespace AuditMemoSystem.Core.Tests;

public sealed class ReleaseVersionTests
{
    [Theory]
    [InlineData("1.0.3")]
    [InlineData("12.34.56-rc.1")]
    [InlineData("1.0.0-alpha-beta.7")]
    public void TryParse_AcceptsStrictSemanticVersions(string input)
    {
        Assert.True(ReleaseVersion.TryParse(input, out var version));
        Assert.Equal(input, version.ToString());
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("v1.0.0")]
    [InlineData("01.0.0")]
    [InlineData("1.0.0-01")]
    [InlineData("1.0.0+build")]
    [InlineData("1.0.0-")]
    public void TryParse_RejectsNonStrictSemanticVersions(string input) =>
        Assert.False(ReleaseVersion.TryParse(input, out _));

    [Fact]
    public void CompareTo_FollowsSemVerPreReleasePrecedence()
    {
        var ordered = new[]
        {
            "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta",
            "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0"
        }.Select(ReleaseVersion.Parse).ToArray();

        for (var index = 0; index < ordered.Length - 1; index++)
            Assert.True(ordered[index].CompareTo(ordered[index + 1]) < 0);
    }
}
