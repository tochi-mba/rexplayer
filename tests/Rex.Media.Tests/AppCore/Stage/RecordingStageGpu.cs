using Rex.Media.Video;

namespace Rex.Media.Tests.AppCore.Stage;

/// <summary>An <see cref="IStageGpu"/> that draws nothing and remembers every call, in order.</summary>
internal sealed class RecordingStageGpu : IStageGpu
{
    private int _next = 1;

    public int Width { get; set; } = 1920;

    public int Height { get; set; } = 1080;

    public List<StageTexture> Made { get; } = [];

    public List<StageTexture> Released { get; } = [];

    public HashSet<int> Live { get; } = [];

    public List<(StageTexture Texture, int Length, int Row)> Uploads { get; } = [];

    /// <summary>Every draw: the program (or "clear"), the target (0 the output), the inputs, the constants, the blend, the count of points.</summary>
    public List<(string Program, int Target, int[] Inputs, float[] Constants, StageBlend Blend, int Count)> Draws { get; } = [];

    public StageTexture Texture(int width, int height, StageFormat format)
    {
        var texture = new StageTexture(_next++, width, height, format);
        Made.Add(texture);
        Live.Add(texture.Id);
        return texture;
    }

    public void Release(StageTexture texture)
    {
        Released.Add(texture);
        Assert.True(Live.Remove(texture.Id), $"texture {texture.Id} released twice or never made");
    }

    public void Upload(StageTexture texture, ReadOnlySpan<byte> pixels, int rowBytes)
    {
        Assert.Contains(texture.Id, Live);
        Uploads.Add((texture, pixels.Length, rowBytes));
    }

    public void Upload(StageTexture texture, ReadOnlySpan<float> values, int rowValues)
    {
        Assert.Contains(texture.Id, Live);
        Uploads.Add((texture, values.Length, rowValues));
    }

    public void Clear(StageTexture? target, float red, float green, float blue, float alpha) =>
        Draws.Add(("clear", target?.Id ?? 0, [], [red, green, blue, alpha], StageBlend.Replace, 0));

    public void Pass(StageProgram program, StageTexture? target, ReadOnlySpan<StageTexture> inputs, ReadOnlySpan<float> constants, StageBlend blend = StageBlend.Replace) =>
        Record(program, target, inputs, constants, blend, 0);

    public void Points(StageProgram program, StageTexture? target, ReadOnlySpan<StageTexture> inputs, int count, ReadOnlySpan<float> constants, StageBlend blend = StageBlend.Add) =>
        Record(program, target, inputs, constants, blend, count);

    private void Record(StageProgram program, StageTexture? target, ReadOnlySpan<StageTexture> inputs, ReadOnlySpan<float> constants, StageBlend blend, int count)
    {
        Assert.True(inputs.Length <= IStageGpu.MaxInputs);
        Assert.True(constants.Length <= IStageGpu.MaxConstants);
        var ids = new int[inputs.Length];
        for (var i = 0; i < inputs.Length; i++)
        {
            Assert.Contains(inputs[i].Id, Live);
            Assert.NotEqual(target?.Id, inputs[i].Id);
            ids[i] = inputs[i].Id;
        }

        if (target is { } drawn)
        {
            Assert.Contains(drawn.Id, Live);
        }

        Draws.Add((program.Name, target?.Id ?? 0, ids, constants.ToArray(), blend, count));
    }
}
