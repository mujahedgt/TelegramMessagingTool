public static class TestAssert
{
    public static void AssertEqual<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception($"{name}: expected '{expected}', actual '{actual}'");
        }
    }

    public static void AssertTrue(bool condition, string name)
    {
        if (!condition)
        {
            throw new Exception($"{name}: expected true");
        }
    }

    public static void AssertFalse(bool condition, string name)
    {
        if (condition)
        {
            throw new Exception($"{name}: expected false");
        }
    }

    public static async Task AssertThrowsAsync<TException>(Func<Task> action, string name)
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
        catch (Exception ex)
        {
            throw new Exception($"{name}: expected {typeof(TException).Name}, actual {ex.GetType().Name}");
        }

        throw new Exception($"{name}: expected {typeof(TException).Name}");
    }
}
