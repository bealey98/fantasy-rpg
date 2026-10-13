using System.Diagnostics;
using System.Reflection;

namespace Battle.Tests;

// A deliberately tiny test framework, so the tests need no packages.
// Mark a method with [Test]; it passes if it returns without throwing.
// Run:  dotnet run --project tests/Battle.Tests -c Release            (all tests)
//       dotnet run --project tests/Battle.Tests -c Release -- Arena   (names containing "Arena")

[AttributeUsage(AttributeTargets.Method)]
public sealed class TestAttribute : Attribute { }

public sealed class AssertionException(string message) : Exception(message);

public static class Assert
{
    public static void True(bool condition, string message = "expected true")
    {
        if (!condition) throw new AssertionException(message);
    }

    public static void False(bool condition, string message = "expected false")
    {
        if (condition) throw new AssertionException(message);
    }

    public static void Equal<T>(T expected, T actual, string? what = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new AssertionException($"{what ?? "value"}: expected {expected}, got {actual}");
    }

    public static void NotEqual<T>(T notExpected, T actual, string? what = null)
    {
        if (EqualityComparer<T>.Default.Equals(notExpected, actual))
            throw new AssertionException($"{what ?? "value"}: did not expect {actual}");
    }

    public static T Throws<T>(Action action, string? what = null) where T : Exception
    {
        try { action(); }
        catch (T e) { return e; }
        catch (Exception e) { throw new AssertionException($"{what ?? "action"}: expected {typeof(T).Name}, got {e.GetType().Name}: {e.Message}"); }
        throw new AssertionException($"{what ?? "action"}: expected {typeof(T).Name}, but nothing was thrown");
    }
}

public static class Program
{
    public static int Main(string[] args)
    {
        string? filter = args.Length > 0 ? args[0] : null;

        var tests = typeof(Program).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttribute<TestAttribute>() != null)
            .Where(m => filter == null || $"{m.DeclaringType!.Name}.{m.Name}".Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.DeclaringType!.Name, StringComparer.Ordinal)
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .ToList();

        int passed = 0;
        var failures = new List<string>();
        var total = Stopwatch.StartNew();

        foreach (var method in tests)
        {
            string name = $"{method.DeclaringType!.Name}.{method.Name}";
            var watch = Stopwatch.StartNew();
            try
            {
                object? instance = method.IsStatic ? null : Activator.CreateInstance(method.DeclaringType!);
                method.Invoke(instance, null);
                passed++;
                if (watch.ElapsedMilliseconds > 500)
                    Console.WriteLine($"  slow  {name} ({watch.ElapsedMilliseconds} ms)");
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                failures.Add($"FAIL  {name}\n      {e.InnerException.GetType().Name}: {e.InnerException.Message}\n{Indent(e.InnerException.StackTrace)}");
            }
        }

        foreach (var failure in failures) Console.WriteLine(failure);
        Console.WriteLine($"{passed} passed, {failures.Count} failed, {tests.Count} tests in {total.ElapsedMilliseconds} ms");
        return failures.Count == 0 ? 0 : 1;
    }

    private static string Indent(string? stack) =>
        string.Join("\n", (stack ?? "").Split('\n').Take(6).Select(l => "      " + l.Trim()));
}
