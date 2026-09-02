namespace Print2Md.Core.Tests;

internal static class AssertEx
{
    public static void Contains(string expected, string actual)
    {
        if (actual.IndexOf(expected, StringComparison.Ordinal) < 0)
        {
            throw new InvalidOperationException($"Expected output to contain: {expected}{Environment.NewLine}Actual:{Environment.NewLine}{actual}");
        }
    }

    public static void DoesNotContain(string unexpected, string actual)
    {
        if (actual.IndexOf(unexpected, StringComparison.Ordinal) >= 0)
        {
            throw new InvalidOperationException($"Expected output not to contain: {unexpected}{Environment.NewLine}Actual:{Environment.NewLine}{actual}");
        }
    }

    public static void Equal<T>(T expected, T actual)
        where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
        }
    }

    public static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static async Task ThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}

