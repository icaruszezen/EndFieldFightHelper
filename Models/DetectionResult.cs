namespace EndFieldFightHelper.Models;

public class DetectionResult
{
    public required string Name { get; init; }
    public float Confidence { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    public string ConfidenceText => $"{Confidence:P1}";
    public string BoundsText => $"({X}, {Y}) {Width}x{Height}";
}
