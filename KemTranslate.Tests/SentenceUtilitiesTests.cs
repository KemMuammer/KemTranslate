namespace KemTranslate.Tests;

internal static class SentenceUtilitiesTests
{
    public static void Run()
    {
        TryGetSentenceRange_ReturnsSentenceAroundOffset();
        TryGetSentenceRangeByOrdinal_ReturnsExpectedSentence();
    }

    private static void TryGetSentenceRange_ReturnsSentenceAroundOffset()
    {
        const string text = "Hello world. Second sentence!";

        var found = SentenceUtilities.TryGetSentenceRange(text, 15, out var start, out var length);

        TestAssert.IsTrue(found, "Expected sentence range to be found.");
        TestAssert.AreEqual("Second sentence!", text.Substring(start, length), "Expected the second sentence to be selected.");
    }

    private static void TryGetSentenceRangeByOrdinal_ReturnsExpectedSentence()
    {
        const string text = "First line. Second line? Third line.";

        var found = SentenceUtilities.TryGetSentenceRangeByOrdinal(text, 1, out var start, out var length);

        TestAssert.IsTrue(found, "Expected sentence ordinal lookup to succeed.");
        TestAssert.AreEqual("Second line?", text.Substring(start, length), "Expected the second sentence by ordinal.");
    }
}
