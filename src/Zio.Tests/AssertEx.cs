namespace Zio.Tests;

internal static class AssertEx
{
    public static void AreEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        CollectionAssert.AreEqual(expected.ToList(), actual.ToList());
    }

    public static void AreEqual<T>(T expected, T actual)
    {
        if (expected is IDictionary expectedDictionary && actual is IDictionary actualDictionary)
        {
            Assert.AreEqual(expectedDictionary.Count, actualDictionary.Count);
            foreach (DictionaryEntry pair in expectedDictionary)
            {
                Assert.IsTrue(actualDictionary.Contains(pair.Key));
                AreEqual(pair.Value, actualDictionary[pair.Key]);
            }

            return;
        }

        if (TryGetEnumerableComparisonInputs(expected, actual, out var expectedValues, out var actualValues))
        {
            CollectionAssert.AreEqual(expectedValues, actualValues);
            return;
        }

        Assert.AreEqual(expected, actual);
    }

    public static void AreEqual<T>(T expected, T actual, IEqualityComparer<T> comparer)
    {
        Assert.IsTrue(comparer.Equals(expected, actual));
    }

    public static void AreNotEqual<T>(T notExpected, T actual)
    {
        if (notExpected is IDictionary expectedDictionary && actual is IDictionary actualDictionary)
        {
            if (!AreDictionariesEqual(expectedDictionary, actualDictionary))
            {
                return;
            }

            Assert.Fail("Values are equal.");
            return;
        }

        if (TryGetEnumerableComparisonInputs(notExpected, actual, out var notExpectedValues, out var actualValues))
        {
            if (notExpectedValues.Count == actualValues.Count && notExpectedValues.SequenceEqual(actualValues))
            {
                Assert.Fail("Values are equal.");
            }

            return;
        }

        Assert.AreNotEqual(notExpected, actual);
    }

    public static void Empty<T>(IEnumerable<T> values)
    {
        Assert.AreEqual(0, values.Count());
    }

    public static void Empty(IEnumerable values)
    {
        Assert.AreEqual(0, values.Cast<object>().Count());
    }

    public static T Single<T>(IEnumerable<T> values)
    {
        var list = values.ToList();
        Assert.AreEqual(1, list.Count);
        return list[0];
    }

    public static void Equivalent<T>(IEnumerable<T> expected, IEnumerable<T> actual)
        where T : IComparable<T>
    {
        AreEqual(expected.OrderBy(i => i), actual.OrderBy(i => i));
    }

    private static bool TryGetEnumerableComparisonInputs<T>(
        T expected,
        T actual,
        out List<object> expectedValues,
        out List<object> actualValues)
    {
        expectedValues = new List<object>();
        actualValues = new List<object>();

        if (expected is null || actual is null || expected is string || actual is string)
        {
            return false;
        }

        if (expected is not IEnumerable expectedEnumerable || actual is not IEnumerable actualEnumerable)
        {
            return false;
        }

        expectedValues = expectedEnumerable.Cast<object>().ToList();
        actualValues = actualEnumerable.Cast<object>().ToList();
        return true;
    }

    private static bool AreDictionariesEqual(IDictionary expected, IDictionary actual)
    {
        if (expected.Count != actual.Count)
        {
            return false;
        }

        foreach (DictionaryEntry pair in expected)
        {
            if (!actual.Contains(pair.Key))
            {
                return false;
            }

            var actualValue = actual[pair.Key];
            if (!(pair.Value?.Equals(actualValue) ?? actualValue is null))
            {
                return false;
            }
        }

        return true;
    }
}
