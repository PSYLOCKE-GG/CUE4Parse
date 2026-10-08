using System;

namespace CUE4Parse_Conversion.Dto;

public sealed class HeightmapRaster
{
    private readonly ushort[] _samples;

    public int Width { get; }
    public int Height { get; }
    public ReadOnlyMemory<ushort> Samples => _samples;

    public HeightmapRaster(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        _samples = new ushort[checked(width * height)];
    }

    public Span<ushort> GetRowSpan(int row)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, Height);
        return _samples.AsSpan(row * Width, Width);
    }
}
