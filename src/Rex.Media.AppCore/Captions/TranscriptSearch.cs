namespace Rex.Media.AppCore.Captions;

/// <summary>
/// Finds speech within a transcript without building a second copy of the entire text. A phrase
/// matches successive words; searching after a hit wraps to the beginning, never across the end
/// of the recording. Views can use the returned word's exact timestamp to seek.
/// </summary>
public static class TranscriptSearch
{
    public static int FindNext(IReadOnlyList<TranscriptWord> words, string query, int after = -1)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(query);
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0 || words.Count == 0 || terms.Length > words.Count)
        {
            return -1;
        }

        var start = Math.Clamp(after, -1, words.Count - 1);
        for (var offset = 1; offset <= words.Count; offset++)
        {
            var index = (start + (long)offset) % words.Count;
            if (index + terms.Length > words.Count)
            {
                continue;
            }

            var found = true;
            for (var term = 0; term < terms.Length; term++)
            {
                if (!words[(int)index + term].Text.Contains(terms[term], StringComparison.CurrentCultureIgnoreCase))
                {
                    found = false;
                    break;
                }
            }

            if (found)
            {
                return (int)index;
            }
        }

        return -1;
    }
}
