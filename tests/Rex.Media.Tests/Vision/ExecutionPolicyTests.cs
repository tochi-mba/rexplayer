using Rex.Media.Vision;

namespace Rex.Media.Tests.Vision;

public sealed class ExecutionPolicyTests
{
    private static readonly Accelerator Processor = new("CPUExecutionProvider", false, 0x8086, 0);
    private static readonly Accelerator Integrated = new(ExecutionPolicy.DirectMl, true, ExecutionPolicy.IntegratedVendor, 0x9b41);
    private static readonly Accelerator Card = new(ExecutionPolicy.DirectMl, true, 0x10de, 0x2504);
    private static readonly Accelerator SecondCard = new(ExecutionPolicy.DirectMl, true, 0x1002, 0x1000);
    private static readonly Accelerator Software = new(ExecutionPolicy.DirectMl, true, ExecutionPolicy.SoftwareVendor, 0x8c);
    private static readonly Accelerator OtherProvider = new("SomeVendorExecutionProvider", true, 0x10de, 0x2504);

    [Fact]
    public void ASeparateCardIsPreferredToAnIntegratedOne()
    {
        Assert.Equal(Card, ExecutionPolicy.ChooseGpu([Processor, Integrated, Card]));
        Assert.Equal(Integrated, ExecutionPolicy.ChooseGpu([Processor, Integrated, Software]));
        Assert.Equal(SecondCard, ExecutionPolicy.ChooseGpu([Card, SecondCard]));
    }

    [Fact]
    public void ProcessorsSoftwareAndOtherProvidersAreNotGraphicsChoices()
    {
        Assert.Null(ExecutionPolicy.ChooseGpu([Processor, Software, OtherProvider]));
        Assert.Throws<ArgumentNullException>(() => ExecutionPolicy.ChooseGpu(null!));
    }

    [Fact]
    public void TheGraphicsProcessorIsUsedUntilItFails()
    {
        Assert.Equal(ExecutionDevice.Gpu, ExecutionPolicy.Prefer(Card, gpuFailed: false));
        Assert.Equal(ExecutionDevice.Cpu, ExecutionPolicy.Prefer(Card, gpuFailed: true));
        Assert.Equal(ExecutionDevice.Cpu, ExecutionPolicy.Prefer(null, gpuFailed: false));
    }
}
