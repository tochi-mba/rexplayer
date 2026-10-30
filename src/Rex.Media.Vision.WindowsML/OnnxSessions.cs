using Microsoft.ML.OnnxRuntime;

namespace Rex.Media.Vision.WindowsML;

/// <summary>
/// Opens ONNX models through Windows ML's ONNX Runtime on the device <see cref="ExecutionPolicy"/>
/// chooses. This class only sequences runtime calls: when the graphics load fails, it asks the
/// policy again with that failure recorded, which answers with the processor.
/// </summary>
public static class OnnxSessions
{
    /// <summary>The devices Windows ML offers, as the policy reads them.</summary>
    public static IReadOnlyList<Accelerator> Accelerators() =>
        [.. Devices().Select(device => device.Info)];

    /// <summary>The device the policy prefers on this computer.</summary>
    public static ExecutionDevice Preferred() =>
        ExecutionPolicy.Prefer(ExecutionPolicy.ChooseGpu(Accelerators()), gpuFailed: false);

    /// <summary>
    /// Opens <paramref name="path"/> on <paramref name="wanted"/>, falling back to the processor when
    /// the graphics load fails. <paramref name="threads"/> above zero caps the processor threads one
    /// run may use, so a model that runs all the time (the camera's) never crowds out the sound and the window.
    /// </summary>
    public static (InferenceSession Session, ExecutionDevice Device) Open(string path, ExecutionDevice wanted, int threads = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var devices = Devices();
        var gpu = ExecutionPolicy.ChooseGpu(devices.Select(device => device.Info));
        if (wanted == ExecutionDevice.Gpu && gpu is not null)
        {
            try
            {
                using var options = GraphicsOptions(devices.First(device => device.Info == gpu).Device);
                Limit(options, threads);
                return (new InferenceSession(path, options), ExecutionDevice.Gpu);
            }
            catch (OnnxRuntimeException)
            {
                // The policy below answers with the processor once the graphics path has failed.
            }
        }

        var device = ExecutionPolicy.Prefer(wanted == ExecutionDevice.Gpu ? gpu : null, gpuFailed: true);
        using var processor = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL, LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
        Limit(processor, threads);
        return (new InferenceSession(path, processor), device);
    }

    private static void Limit(SessionOptions options, int threads)
    {
        if (threads > 0)
        {
            options.IntraOpNumThreads = threads;
            options.InterOpNumThreads = 1;
        }
    }

    private static SessionOptions GraphicsOptions(OrtEpDevice device)
    {
        // DirectML runs one operator at a time and does not reuse memory patterns across runs.
        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            EnableMemoryPattern = false,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
        };
        try
        {
            options.AppendExecutionProvider(OrtEnv.Instance(), [device], new Dictionary<string, string>());
            return options;
        }
        catch
        {
            options.Dispose();
            throw;
        }
    }

    private static List<(Accelerator Info, OrtEpDevice Device)> Devices() =>
        [.. OrtEnv.Instance().GetEpDevices().Select(device => (
            new Accelerator(device.EpName, device.HardwareDevice.Type == OrtHardwareDeviceType.GPU, device.HardwareDevice.VendorId, device.HardwareDevice.DeviceId),
            device))];
}
