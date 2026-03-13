using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace KemTranslate
{
    internal static class DiffUtilities
    {
        private static readonly Regex DiffTokenRegex = new(@"\w+|\s+|[^\w\s]+", RegexOptions.Compiled);

        public static LanguageToolWriteResult BuildDiffWriteResult(string originalText, string updatedText)
        {
            var originalTokens = TokenizeForDiff(originalText);
            var updatedTokens = TokenizeForDiff(updatedText);
            var commonUpdated = GetCommonUpdatedTokenFlags(originalTokens, updatedTokens);
            var segments = new List<LanguageToolSegment>();

            for (int i = 0; i < updatedTokens.Count; i++)
            {
                bool isChanged = !commonUpdated[i] && !string.IsNullOrWhiteSpace(updatedTokens[i]);
                if (segments.Count > 0 && segments[^1].IsChanged == isChanged)
                {
                    segments[^1].Text += updatedTokens[i];
                    continue;
                }

                segments.Add(new LanguageToolSegment { Text = updatedTokens[i], IsChanged = isChanged });
            }

            if (segments.Count == 0)
                segments.Add(new LanguageToolSegment { Text = updatedText });

            return new LanguageToolWriteResult
            {
                CorrectedText = updatedText,
                Segments = segments
            };
        }

        public static List<string> TokenizeForDiff(string text)
        {
            if (string.IsNullOrEmpty(text))
                return [];

            var matches = DiffTokenRegex.Matches(text);
            if (matches.Count == 0)
                return [text];

            return matches.Select(x => x.Value).ToList();
        }

        public static bool[] GetCommonUpdatedTokenFlags(IReadOnlyList<string> originalTokens, IReadOnlyList<string> updatedTokens)
        {
            int n = originalTokens.Count;
            int m = updatedTokens.Count;
            var dp = new int[n + 1, m + 1];

            for (int i = n - 1; i >= 0; i--)
            {
                for (int j = m - 1; j >= 0; j--)
                {
                    if (string.Equals(originalTokens[i], updatedTokens[j], StringComparison.Ordinal))
                        dp[i, j] = dp[i + 1, j + 1] + 1;
                    else
                        dp[i, j] = Math.Max(dp[i + 1, j], dp[i, j + 1]);
                }
            }

            var commonUpdated = new bool[m];
            int x = 0;
            int y = 0;
            while (x < n && y < m)
            {
                if (string.Equals(originalTokens[x], updatedTokens[y], StringComparison.Ordinal))
                {
                    commonUpdated[y] = true;
                    x++;
                    y++;
                }
                else if (dp[x + 1, y] >= dp[x, y + 1])
                {
                    x++;
                }
                else
                {
                    y++;
                }
            }

            return commonUpdated;
        }
    }
}
