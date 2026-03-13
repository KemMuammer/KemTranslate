using System;
using System.Collections.Generic;

namespace KemTranslate.Tests;

internal static class TestAssert
{
    public static void IsTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    public static void AreEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message} Expected: {expected}; Actual: {actual}");
    }

    public static void SequenceEqual<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, string message)
    {
        if (expected.Count != actual.Count)
            throw new InvalidOperationException($"{message} Count mismatch. Expected: {expected.Count}; Actual: {actual.Count}");

        for (int i = 0; i < expected.Count; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(expected[i], actual[i]))
                throw new InvalidOperationException($"{message} Mismatch at index {i}. Expected: {expected[i]}; Actual: {actual[i]}");
        }
    }
}
