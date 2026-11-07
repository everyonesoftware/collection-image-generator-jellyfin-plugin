using Xunit;
namespace CollectionImageGenerator.Core.Tests;
public class PolicyTests
{
    [Theory]
    [InlineData(null, null, false, true)]
    [InlineData("", null, false, true)]
    [InlineData("custom", null, false, false)]
    [InlineData("custom", "generated", false, false)]
    [InlineData("ABCD", "abcd", false, true)]
    [InlineData("custom", "generated", true, true)]
    public void PreserveUnknownUnlessMissingOwnedOrExplicit(string? current, string? owned, bool overwrite, bool expected)
        => Assert.Equal(expected, ArtworkPolicy.CanReplace(current, owned, overwrite));
}
