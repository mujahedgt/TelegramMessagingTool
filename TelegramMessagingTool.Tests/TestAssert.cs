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
}
