using System;

namespace KemTranslate
{
    internal static class SentenceUtilities
    {
        public static bool TryGetSentenceRange(string text, int offset, out int start, out int length)
        {
            start = -1;
            length = 0;

            if (string.IsNullOrWhiteSpace(text) || offset < 0 || offset >= text.Length)
                return false;

            int sentenceStart = offset;
            while (sentenceStart > 0 && !IsSentenceBoundary(text[sentenceStart - 1]))
                sentenceStart--;

            int sentenceEnd = offset;
            while (sentenceEnd < text.Length && !IsSentenceBoundary(text[sentenceEnd]))
                sentenceEnd++;

            if (sentenceEnd < text.Length && (text[sentenceEnd] == '.' || text[sentenceEnd] == '!' || text[sentenceEnd] == '?'))
                sentenceEnd++;

            while (sentenceStart < sentenceEnd && char.IsWhiteSpace(text[sentenceStart]))
                sentenceStart++;

            while (sentenceEnd > sentenceStart && char.IsWhiteSpace(text[sentenceEnd - 1]))
                sentenceEnd--;

            if (sentenceEnd <= sentenceStart)
                return false;

            start = sentenceStart;
            length = sentenceEnd - sentenceStart;
            return true;
        }

        public static bool TryGetSentenceOrdinal(string text, int offset, out int ordinal)
        {
            ordinal = -1;
            if (string.IsNullOrWhiteSpace(text) || offset < 0 || offset >= text.Length)
                return false;

            int cursor = 0;
            int index = 0;

            while (TryGetSentenceRangeByCursor(text, ref cursor, out var start, out var length))
            {
                if (offset >= start && offset < start + length)
                {
                    ordinal = index;
                    return true;
                }

                index++;
            }

            return false;
        }

        public static bool TryGetSentenceRangeByOrdinal(string text, int ordinal, out int start, out int length)
        {
            start = -1;
            length = 0;

            if (ordinal < 0 || string.IsNullOrWhiteSpace(text))
                return false;

            int cursor = 0;
            int index = 0;

            while (TryGetSentenceRangeByCursor(text, ref cursor, out start, out length))
            {
                if (index == ordinal)
                    return true;

                index++;
            }

            start = -1;
            length = 0;
            return false;
        }

        public static bool TryGetSentenceRangeByCursor(string text, ref int cursor, out int start, out int length)
        {
            start = -1;
            length = 0;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
                cursor++;

            if (cursor >= text.Length)
                return false;

            int sentenceStart = cursor;
            while (cursor < text.Length && !IsSentenceBoundary(text[cursor]))
                cursor++;

            int sentenceEnd = cursor;
            if (cursor < text.Length && (text[cursor] == '.' || text[cursor] == '!' || text[cursor] == '?'))
            {
                sentenceEnd = cursor + 1;
                cursor++;
            }

            while (sentenceStart < sentenceEnd && char.IsWhiteSpace(text[sentenceStart]))
                sentenceStart++;

            while (sentenceEnd > sentenceStart && char.IsWhiteSpace(text[sentenceEnd - 1]))
                sentenceEnd--;

            if (sentenceEnd <= sentenceStart)
                return TryGetSentenceRangeByCursor(text, ref cursor, out start, out length);

            start = sentenceStart;
            length = sentenceEnd - sentenceStart;
            return true;
        }

        public static bool IsSentenceBoundary(char c)
        {
            return c == '.' || c == '!' || c == '?' || c == '\r' || c == '\n';
        }
    }
}
