namespace KemTranslate.Tests;

internal static class DiffUtilitiesTests
{
    public static void Run()
    {
        BuildDiffWriteResult_MarksChangedSegments();
        TokenizeForDiff_SplitsWordsWhitespaceAndPunctuation();
    }

    private static void BuildDiffWriteResult_MarksChangedSegments()
    {
        var result = DiffUtilities.BuildDiffWriteResult("Hello world", "Hello brave world");

        TestAssert.AreEqual("Hello brave world", result.CorrectedText, "Expected corrected text to match updated text.");
        TestAssert.IsTrue(result.Segments.Exists(x => x.IsChanged && x.Text.Contains("brave")), "Expected changed segment to include inserted text.");
    }

    private static void TokenizeForDiff_SplitsWordsWhitespaceAndPunctuation()
    {
        var tokens = DiffUtilities.TokenizeForDiff("Hi, all!");

        TestAssert.SequenceEqual(new[] { "Hi", ",", " ", "all", "!" }, tokens, "Expected punctuation and whitespace tokenization.");
    }
}
