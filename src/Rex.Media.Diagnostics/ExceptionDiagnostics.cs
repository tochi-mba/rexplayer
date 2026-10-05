using System.Reflection;

namespace Rex.Media.Diagnostics;

/// <summary>
/// Turns an exception into the line a person needs: the real cause, not the reflection or task
/// wrapper around it.
/// </summary>
public static class ExceptionDiagnostics
{
    /// <summary>The innermost meaningful exception: wrappers with exactly one cause are peeled off.</summary>
    public static Exception RootCause(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var current = exception;
        while (true)
        {
            switch (current)
            {
                case TargetInvocationException { InnerException: { } inner }:
                    current = inner;
                    continue;
                case AggregateException aggregate when aggregate.InnerExceptions.Count == 1:
                    current = aggregate.InnerExceptions[0];
                    continue;
                default:
                    return current;
            }
        }
    }

    /// <summary>"TypeName: message" of the root cause.</summary>
    public static string Summary(Exception exception)
    {
        var root = RootCause(exception);
        return root.GetType().Name + ": " + root.Message;
    }
}
