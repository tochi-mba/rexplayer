namespace Rex.Media.TestKit;

/// <summary>
/// Declares that a test proves a row of the capability matrix (docs/capability-matrix.json).
/// A repository test reads these attributes from every test assembly, so a row can only be marked
/// verified while a test that proves it still exists, and an attribute can only name a real row.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class CapabilityAttribute : Attribute
{
    public CapabilityAttribute(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
    }

    public string Id { get; }
}
