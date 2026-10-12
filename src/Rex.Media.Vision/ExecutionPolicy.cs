namespace Rex.Media.Vision;

/// <summary>Where a model runs.</summary>
public enum ExecutionDevice
{
    Cpu,
    Gpu,
}

/// <summary>
/// One device Windows ML offers to run a model on, as the adapter reads it from the runtime:
/// the execution provider's name, whether the hardware is a graphics processor, and its PCI ids.
/// </summary>
public sealed record Accelerator(string Provider, bool IsGpu, uint VendorId, uint DeviceId);

/// <summary>
/// Which device a model should run on. A graphics processor through DirectML is preferred when
/// one is present, because the image encoder is a few hundred million multiply-adds a picture;
/// otherwise, or after the graphics path has failed once, the processor runs it.
/// </summary>
public static class ExecutionPolicy
{
    /// <summary>The execution provider Windows ML uses for Direct3D 12 graphics processors.</summary>
    public const string DirectMl = "DmlExecutionProvider";

    /// <summary>The software rasterizer's vendor id: a graphics device in name only.</summary>
    public const uint SoftwareVendor = 0x1414;

    /// <summary>Intel's vendor id. Its parts are usually integrated, so a separate card is tried first.</summary>
    public const uint IntegratedVendor = 0x8086;

    /// <summary>The graphics processor to use, or null when only the processor is available.</summary>
    public static Accelerator? ChooseGpu(IEnumerable<Accelerator> available)
    {
        ArgumentNullException.ThrowIfNull(available);
        return available
            .Where(device => device.IsGpu && device.Provider == DirectMl && device.VendorId != SoftwareVendor)
            .OrderBy(device => device.VendorId == IntegratedVendor ? 1 : 0)
            .ThenBy(device => device.DeviceId)
            .FirstOrDefault();
    }

    /// <summary>The device to load a model on, given the graphics processor found and whether loading on it has already failed.</summary>
    public static ExecutionDevice Prefer(Accelerator? gpu, bool gpuFailed) =>
        gpu is not null && !gpuFailed ? ExecutionDevice.Gpu : ExecutionDevice.Cpu;
}
