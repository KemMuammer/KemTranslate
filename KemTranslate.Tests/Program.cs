using System;

namespace KemTranslate.Tests;

internal static class Program
{
    private static int Main()
    {
        try
        {
            SentenceUtilitiesTests.Run();
            DiffUtilitiesTests.Run();
            TargetLanguageHelperTests.Run();
            WindowsStartupHelperTests.Run();
            Console.WriteLine("All tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
