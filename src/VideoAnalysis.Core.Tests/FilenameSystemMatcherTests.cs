using VideoAnalysis.Core.Domain;
using Xunit;

namespace VideoAnalysis.Core.Tests;

public class FilenameSystemMatcherTests
{
    [Fact]
    public void BuildCandidateQueries_ShrinksFromFullFilenameDownToFirstWord()
    {
        var candidates = FilenameSystemMatcher.BuildCandidateQueries("Merope A Ring_v2");

        Assert.Equal(
            new[] { "Merope A Ring v2", "Merope A Ring", "Merope A", "Merope" },
            candidates);
    }

    [Fact]
    public void BuildCandidateQueries_SkipsPhrasesShorterThanThreeCharacters()
    {
        var candidates = FilenameSystemMatcher.BuildCandidateQueries("Ab");

        Assert.Empty(candidates);
    }

    [Fact]
    public void BuildCandidateQueries_RespectsMaxCandidatesCap()
    {
        var candidates = FilenameSystemMatcher.BuildCandidateQueries("one two three four five six seven eight nine ten", maxCandidates: 3);

        Assert.Equal(3, candidates.Count);
        Assert.Equal("one two three four five six seven eight nine ten", candidates[0]);
    }

    [Theory]
    [InlineData("Merope", "Merope A Ring_v2.mp4", true)]
    [InlineData("Merope", "merope_a_ring.mp4", true)]
    [InlineData("Col 285 Sector", "Col 285 Sector AB-C d1-23 1 A Ring.mp4", true)]
    [InlineData("Sol", "Solati Ring.mp4", false)]
    [InlineData("Deciat", "Merope A Ring.mp4", false)]
    public void IsNameInFilename_MatchesIgnoringCaseAndSeparators(string systemName, string fileName, bool expected)
    {
        Assert.Equal(expected, FilenameSystemMatcher.IsNameInFilename(systemName, fileName));
    }
}
