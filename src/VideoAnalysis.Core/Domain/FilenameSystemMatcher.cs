using System.Text.RegularExpressions;

namespace VideoAnalysis.Core.Domain;

/// <summary>Heuristics for spotting a system name embedded in an uploaded video's filename, so the
/// upload modal can pre-fill the system search instead of making the user re-type/re-search it -
/// this mirrors <see cref="Storage.VideoFileNamer"/>'s own convention of naming files after the
/// ring (which itself starts with the system name), so the system name is expected as the leading
/// words of the filename, with body/ring/date/take-number details trailing after it.</summary>
public static class FilenameSystemMatcher
{
    private static readonly Regex NonWordRun = new(@"[^\p{L}\p{Nd}]+", RegexOptions.Compiled);

    /// <summary>Builds candidate typeahead queries from the filename's leading words, longest
    /// phrase first, shrinking one word at a time. Candidates shorter than 3 characters are
    /// skipped since that's the same minimum the live typeahead search enforces.</summary>
    public static IReadOnlyList<string> BuildCandidateQueries(string fileName, int maxCandidates = 8)
    {
        var tokens = NonWordRun.Split(fileName).Where(t => t.Length > 0).ToArray();
        var candidates = new List<string>();

        for (var len = tokens.Length; len >= 1 && candidates.Count < maxCandidates; len--)
        {
            var phrase = string.Join(" ", tokens.Take(len));
            if (phrase.Length >= 3)
            {
                candidates.Add(phrase);
            }
        }

        return candidates;
    }

    /// <summary>True if <paramref name="systemName"/> actually occurs in the filename as a whole
    /// sequence of words, ignoring case and treating any run of non-alphanumeric characters
    /// (spaces, underscores, dashes, dots, ...) as equivalent. Matching on word boundaries (rather
    /// than a raw substring) avoids false positives like "Sol" matching inside "Solati" - and is
    /// needed anyway because the typeahead endpoint matches loosely and can return systems that
    /// merely share a prefix with the query, not the filename itself.</summary>
    public static bool IsNameInFilename(string systemName, string fileName)
    {
        var normalizedName = Normalize(systemName);
        var normalizedFileName = Normalize(fileName);
        return normalizedName.Length > 0 &&
            $" {normalizedFileName} ".Contains($" {normalizedName} ", StringComparison.Ordinal);
    }

    private static string Normalize(string value) => NonWordRun.Replace(value, " ").Trim().ToLowerInvariant();
}
