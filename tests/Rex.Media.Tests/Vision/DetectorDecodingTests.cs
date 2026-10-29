using Rex.Media.Vision;

namespace Rex.Media.Tests.Vision;

public sealed class DetectorDecodingTests
{
    // The SHA-256 of each table as OpenCV's zoo publishes it in its own scripts (mp_palmdet.py,
    // mp_persondet.py at the commits ADR-0022 pins), hashed one anchor a line as "x y" to six places.
    private const string PalmTable = "8685775be653368a4aadb0bc6684d1b35f8a9474cb2b98a822de7a52f5ba00f3";
    private const string PersonTable = "b49a22374bbbab20023e7a788cea492ac4c3bfb84aa9c76ec3cb751721c0b641";

    [Fact]
    public void TheGeneratedAnchorsAreThePublishedTables()
    {
        Assert.Equal(2016, AnchorGrid.Palm.Count);
        Assert.Equal(PalmTable, AnchorGrid.Palm.Fingerprint());
        Assert.Equal(2254, AnchorGrid.Person.Count);
        Assert.Equal(PersonTable, AnchorGrid.Person.Fingerprint());
    }

    [Fact]
    public void AnchorsRunAcrossThenDownRepeatedPerCell()
    {
        var grid = new AnchorGrid(16, [(8, 2), (16, 1)]);
        Assert.Equal(9, grid.Count);
        Assert.Equal([0.25f, 0.25f, 0.25f, 0.25f, 0.75f, 0.25f, 0.75f, 0.25f, 0.25f, 0.75f, 0.25f, 0.75f, 0.75f, 0.75f, 0.75f, 0.75f, 0.5f, 0.5f], grid.Anchors);
        Assert.Equal(16, grid.InputSide);
        Assert.Equal(2, grid.Layers.Count);
    }

    [Fact]
    public void AGridIsChecked()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnchorGrid(0, [(8, 2)]));
        Assert.Throws<ArgumentNullException>(() => new AnchorGrid(16, null!));
        Assert.Throws<ArgumentException>(() => new AnchorGrid(16, []));
        Assert.Throws<ArgumentException>(() => new AnchorGrid(16, [(5, 2)]));
        Assert.Throws<ArgumentException>(() => new AnchorGrid(16, [(8, 0)]));
    }

    [Fact]
    public void ADetectionMeasuresItselfAndItsOverlap()
    {
        var a = new Detection(0) { Left = 0.1f, Top = 0.1f, Right = 0.5f, Bottom = 0.3f, Score = 0.9f };
        var b = new Detection(0) { Left = 0.3f, Top = 0.1f, Right = 0.7f, Bottom = 0.3f };
        var far = new Detection(0) { Left = 0.8f, Top = 0.8f, Right = 0.9f, Bottom = 0.9f };

        Assert.Equal(0.4f, a.Width, 5);
        Assert.Equal(0.2f, a.Height, 5);
        Assert.Equal(0.3f, a.CentreX, 5);
        Assert.Equal(0.2f, a.CentreY, 5);
        Assert.Equal(0.08f, a.Area, 5);
        // Shared 0.2 by 0.2 of a union 0.08 + 0.08 - 0.04.
        Assert.Equal(1 / 3f, a.OverlapWith(b), 4);
        Assert.Equal(0, a.OverlapWith(far));
        Assert.Equal(0, new Detection(0).OverlapWith(new Detection(0)));
        Assert.Throws<ArgumentNullException>(() => a.OverlapWith(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Detection(-1));
    }

    /// <summary>A detector output with one object per listed anchor: the box offsets in pixels and a confident logit.</summary>
    private static (float[] Boxes, float[] Scores) Output(AnchorGrid grid, int points, params (int Anchor, float Dx, float Dy, float W, float H, float Logit)[] objects)
    {
        var row = 4 + (points * 2);
        var boxes = new float[grid.Count * row];
        var scores = new float[grid.Count];
        Array.Fill(scores, -9f);
        foreach (var (anchor, dx, dy, w, h, logit) in objects)
        {
            scores[anchor] = logit;
            boxes[anchor * row] = dx;
            boxes[(anchor * row) + 1] = dy;
            boxes[(anchor * row) + 2] = w;
            boxes[(anchor * row) + 3] = h;
            for (var p = 0; p < points; p++)
            {
                boxes[(anchor * row) + 4 + (p * 2)] = dx + p;
                boxes[(anchor * row) + 5 + (p * 2)] = dy - p;
            }
        }

        return (boxes, scores);
    }

    [Fact]
    public void ABoxIsAnOffsetFromItsAnchorInPixelsOfTheSquare()
    {
        var grid = new AnchorGrid(16, [(8, 1)]);
        var decoder = new DetectorDecoder(grid, points: 2, keep: 4);
        // Anchor 3 is the cell at (0.75, 0.75): a box 4 by 2 pixels, shifted 2 right and 1 up.
        var (boxes, scores) = Output(grid, 2, (3, 2, -1, 4, 2, 3f));

        decoder.Decode(boxes, scores, 0.5f, 0.3f);

        Assert.Equal(1, decoder.Count);
        var found = decoder.Detections[0];
        Assert.Equal(0.75f + 0.125f - 0.125f, found.Left, 5);
        Assert.Equal(0.75f - 0.0625f - 0.0625f, found.Top, 5);
        Assert.Equal(0.25f, found.Width, 5);
        Assert.Equal(0.125f, found.Height, 5);
        Assert.Equal(1 / (1 + Math.Exp(-3)), found.Score, 4);
        Assert.Equal([0.875f, 0.6875f, 0.9375f, 0.625f], found.Points);
        Assert.Equal(8, decoder.RowLength);
    }

    [Fact]
    public void LowScoresAreLeftOutAndTheBestComesFirst()
    {
        var grid = new AnchorGrid(16, [(8, 1)]);
        var decoder = new DetectorDecoder(grid, 0, 4);
        var (boxes, scores) = Output(grid, 0, (0, 0, 0, 2, 2, -1f), (1, 0, 0, 2, 2, 1f), (2, 0, 0, 2, 2, 4f));

        decoder.Decode(boxes, scores, 0.6f, 0.3f);

        Assert.Equal(2, decoder.Count);
        Assert.Equal(0.25f, decoder.Detections[0].CentreX, 5);
        Assert.Equal(0.75f, decoder.Detections[0].CentreY, 5);
        Assert.Equal(0.75f, decoder.Detections[1].CentreX, 5);
        Assert.Equal(0.25f, decoder.Detections[1].CentreY, 5);
    }

    [Fact]
    public void OverlappingDetectionsKeepOnlyTheBest()
    {
        var grid = new AnchorGrid(16, [(8, 2)]);
        var decoder = new DetectorDecoder(grid, 0, 4);
        // Two anchors of the same cell, both claiming the same big box; the better one stays.
        var (boxes, scores) = Output(grid, 0, (0, 0, 0, 8, 8, 2f), (1, 0.5f, 0, 8, 8, 3f), (6, 0, 0, 4, 4, 2f));

        decoder.Decode(boxes, scores, 0.5f, 0.5f);

        Assert.Equal(2, decoder.Count);
        Assert.Equal(0.25f + (0.5f / 16), decoder.Detections[0].CentreX, 5);
        Assert.Equal(0.75f, decoder.Detections[1].CentreY, 5);
    }

    [Fact]
    public void AtMostKeepAreGivenBackAndTheRestAreUntouched()
    {
        var grid = new AnchorGrid(16, [(8, 1)]);
        var decoder = new DetectorDecoder(grid, 0, 1);
        var (boxes, scores) = Output(grid, 0, (0, 0, 0, 2, 2, 2f), (3, 0, 0, 2, 2, 3f));

        decoder.Decode(boxes, scores, 0.5f, 0.5f);

        Assert.Equal(1, decoder.Count);
        Assert.Equal(0.75f, decoder.Detections[0].CentreX, 5);
        Assert.Single(decoder.Detections);
    }

    [Fact]
    public void MoreCandidatesThanTheListHoldsDropTheWorst()
    {
        var grid = new AnchorGrid(64, [(8, 1)]);
        var decoder = new DetectorDecoder(grid, 0, 2);
        var objects = new (int, float, float, float, float, float)[grid.Count];
        for (var i = 0; i < grid.Count; i++)
        {
            // Every cell confident, the last cell most of all; cells do not overlap.
            objects[i] = (i, 0, 0, 4, 4, 1 + (i / 64f));
        }

        var (boxes, scores) = Output(grid, 0, objects);
        decoder.Decode(boxes, scores, 0.5f, 0.5f);

        Assert.Equal(2, decoder.Count);
        Assert.Equal(1 / (1 + Math.Exp(-(1 + (63 / 64f)))), decoder.Detections[0].Score, 4);
        Assert.Equal(1 / (1 + Math.Exp(-(1 + (62 / 64f)))), decoder.Detections[1].Score, 4);
    }

    [Fact]
    public void NothingFoundIsCountZeroAndTheOutputShapeIsChecked()
    {
        var grid = new AnchorGrid(16, [(8, 1)]);
        var decoder = new DetectorDecoder(grid, 1, 2);
        var (boxes, scores) = Output(grid, 1);
        decoder.Decode(boxes, scores, 0.5f, 0.5f);
        Assert.Equal(0, decoder.Count);

        Assert.Throws<ArgumentException>(() => decoder.Decode(boxes, scores.AsSpan(1), 0.5f, 0.5f));
        Assert.Throws<ArgumentException>(() => decoder.Decode(boxes.AsSpan(2), scores, 0.5f, 0.5f));
        Assert.Throws<ArgumentNullException>(() => new DetectorDecoder(null!, 1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DetectorDecoder(grid, -1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DetectorDecoder(grid, 1, 0));
    }
}
