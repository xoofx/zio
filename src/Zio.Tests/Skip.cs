namespace Zio.Tests;

internal static class Skip
{
    public static void If(bool condition, string message)
    {
        if (condition)
        {
            Assert.Inconclusive(message);
        }
    }

    public static void IfNot(bool condition, string message)
    {
        if (!condition)
        {
            Assert.Inconclusive(message);
        }
    }
}
