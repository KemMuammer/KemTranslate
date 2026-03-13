namespace KemTranslate.Tests;

internal static class WindowsStartupHelperTests
{
    public static void Run()
    {
        GetExecutablePath_ReturnsPathFromQuotedCommand();
        GetExecutablePath_ReturnsPathFromUnquotedCommand();
        IsStartupApprovedEnabled_TreatsMissingValueAsEnabled();
        IsStartupApprovedEnabled_DetectsDisabledState();
    }

    private static void GetExecutablePath_ReturnsPathFromQuotedCommand()
    {
        const string command = "\"C:\\Program Files\\KemTranslate\\KemTranslate.exe\" --tray";

        var path = WindowsStartupHelper.GetExecutablePath(command);

        AssertEqual("C:\\Program Files\\KemTranslate\\KemTranslate.exe", path, "Expected quoted startup command to be parsed correctly.");
    }

    private static void GetExecutablePath_ReturnsPathFromUnquotedCommand()
    {
        const string command = "C:\\KemTranslate\\KemTranslate.exe --tray";

        var path = WindowsStartupHelper.GetExecutablePath(command);

        AssertEqual("C:\\KemTranslate\\KemTranslate.exe", path, "Expected unquoted startup command to be parsed correctly.");
    }

    private static void IsStartupApprovedEnabled_TreatsMissingValueAsEnabled()
    {
        AssertTrue(WindowsStartupHelper.IsStartupApprovedEnabled(null), "Expected a missing StartupApproved value to default to enabled.");
        AssertTrue(WindowsStartupHelper.IsStartupApprovedEnabled([]), "Expected an empty StartupApproved value to default to enabled.");
    }

    private static void IsStartupApprovedEnabled_DetectsDisabledState()
    {
        AssertTrue(WindowsStartupHelper.IsStartupApprovedEnabled([0x02, 0, 0, 0]), "Expected 0x02 StartupApproved state to be enabled.");
        AssertTrue(WindowsStartupHelper.IsStartupApprovedEnabled([0x06, 0, 0, 0]), "Expected 0x06 StartupApproved state to be enabled.");
        AssertTrue(!WindowsStartupHelper.IsStartupApprovedEnabled([0x03, 0, 0, 0]), "Expected 0x03 StartupApproved state to be disabled.");
        AssertTrue(!WindowsStartupHelper.IsStartupApprovedEnabled([0x07, 0, 0, 0]), "Expected 0x07 StartupApproved state to be disabled.");
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new global::System.InvalidOperationException(message);
    }

    private static void AssertEqual(string expected, string? actual, string message)
    {
        if (!string.Equals(expected, actual, global::System.StringComparison.Ordinal))
            throw new global::System.InvalidOperationException($"{message} Expected: {expected}; Actual: {actual}");
    }
}
