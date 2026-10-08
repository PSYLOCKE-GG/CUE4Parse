using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using CUE4Parse_Conversion.Dto;
using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Toolboxs.Extensions;
using Sdcb.FFmpeg.Utils;

namespace CUE4Parse_Conversion.Textures;

internal static class PngRasterEncoder
{
    public static unsafe byte[] EncodeHeightmap(HeightmapRaster raster, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(raster);
        cancellationToken.ThrowIfCancellationRequested();
        using var encoder = CreateEncoder(raster.Width, raster.Height);
        using var frame = Frame.CreateVideo(raster.Width, raster.Height, AVPixelFormat.Gray16be);
        for (var y = 0; y < raster.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = new Span<byte>((byte*)frame.Data[0] + y * frame.Linesize[0], checked(raster.Width * 2));
            var source = raster.GetRowSpan(y);
            for (var x = 0; x < raster.Width; x++)
                BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(x * 2, 2), source[x]);
        }
        frame.Pts = 0;
        using var packet = new Packet();
        using var output = new MemoryStream();
        foreach (var encoded in encoder.EncodeFrame(frame, packet, unref: false))
        {
            output.Write(new ReadOnlySpan<byte>(((AVPacket*)encoded)->data, ((AVPacket*)encoded)->size));
            encoded.Unref();
        }
        foreach (var encoded in encoder.EncodeFrame(null, packet, unref: false))
        {
            output.Write(new ReadOnlySpan<byte>(((AVPacket*)encoded)->data, ((AVPacket*)encoded)->size));
            encoded.Unref();
        }
        if (output.Length == 0) throw new InvalidOperationException("The PNG encoder produced no image.");
        return output.ToArray();
    }

    private static CodecContext CreateEncoder(int width, int height)
    {
        var codec = Codec.FindEncoderByName("png")
            ?? throw new InvalidOperationException("The loaded libavcodec has no PNG encoder.");
        var encoder = new CodecContext(codec)
        {
            Width = width,
            Height = height,
            PixelFormat = AVPixelFormat.Gray16be,
            TimeBase = new AVRational { Num = 1, Den = 1000 },
            ThreadCount = 1,
            CompressionLevel = 6,
        };
        try
        {
            encoder.PrivateOptions.Set("pred", "mixed");
            encoder.Open();
            return encoder;
        }
        catch
        {
            encoder.Dispose();
            throw;
        }
    }
}
