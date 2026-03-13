namespace KemTranslate.Tests;

internal static class TargetLanguageHelperTests
{
    public static void Run()
    {
        TryGetDifferentTargetCode_PrefersEnglishWhenSourceIsNotEnglish();
        TryGetDifferentTargetCode_FallsBackToAnotherLanguageWhenEnglishIsSource();
    }

    private static void TryGetDifferentTargetCode_PrefersEnglishWhenSourceIsNotEnglish()
    {
        var languages = new[]
        {
            new LtLanguage("en", "English"),
            new LtLanguage("de", "German"),
            new LtLanguage("fr", "French")
        };

        var found = TargetLanguageHelper.TryGetDifferentTargetCode(languages, "de", "de", out var newTarget);

        TestAssert.IsTrue(found, "Expected a replacement language to be found.");
        TestAssert.AreEqual("en", newTarget, "Expected English to be preferred when source is not English.");
    }

    private static void TryGetDifferentTargetCode_FallsBackToAnotherLanguageWhenEnglishIsSource()
    {
        var languages = new[]
        {
            new LtLanguage("en", "English"),
            new LtLanguage("fr", "French")
        };

        var found = TargetLanguageHelper.TryGetDifferentTargetCode(languages, "en", "en", out var newTarget);

        TestAssert.IsTrue(found, "Expected a fallback language to be found.");
        TestAssert.AreEqual("fr", newTarget, "Expected the non-source language fallback to be selected.");
    }
}
