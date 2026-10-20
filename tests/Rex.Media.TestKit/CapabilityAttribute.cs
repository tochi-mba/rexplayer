namespace Rex.Media.TestKit;

/// <summary>
/// Which platform a test's proof holds on. A test in the portable suite proves its row everywhere it
/// runs; a test in a platform-specific project proves it only there, and says so.
/// </summary>
public enum CapabilityPlatform
{
    /// <summary>Every platform the suite runs on; the default, and the only honest value for a portable test.</summary>
    All,

    Windows,

    MacOS,
}

/// <summary>
/// Declares that a test proves a row of the capability matrix (docs/capability-matrix.json) on a
/// platform. A repository test reads these attributes from every test assembly, so a row can only
/// be marked verified on a platform while a test that proves it there still exists, and an
/// attribute can only name a real row.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class CapabilityAttribute : Attribute
{
    public CapabilityAttribute(string id)
        : this(id, CapabilityPlatform.All)
    {
    }

    public CapabilityAttribute(string id, CapabilityPlatform platform)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
        Platform = platform;
    }

    public string Id { get; }

    public CapabilityPlatform Platform { get; }
}
