using System;
using System.IO;
using System.Threading;
using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Formats;
using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Toolboxs.Extensions;
using Sdcb.FFmpeg.Utils;

namespace CUE4Parse_Conversion.Textures;

public sealed class AnimatedPngEncoder : IDisposable
{
    private readonly MemoryStream _output = new();
    private readonly IOContext _io;
    private readonly FormatContext _muxer;
    private readonly CodecContext _encoder;
    private readonly MediaStream _stream;
    private readonly Frame _frame;
    private readonly Packet _packet;
    private readonly int _rowBytes;
    private readonly int _height;
    private readonly int _finalFrameDelayMs;
    private long _pts;
    private int _frameCount;
    private int _lastFrameDelayMs;
    private bool _finished;
    private bool _disposed;

    public unsafe AnimatedPngEncoder(int width, int height, int finalFrameDelayMs)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ValidateDelay(finalFrameDelayMs);
        _rowBytes = checked(width * 4);
        _ = checked(_rowBytes * height);
        _height = height;
        _finalFrameDelayMs = finalFrameDelayMs;
        try
        {
            _encoder = PngRasterEncoder.CreateEncoder("apng", width, height, AVPixelFormat.Rgba);
            _io = IOContext.WriteStream(_output);
            _muxer = FormatContext.AllocOutput(formatName: "apng");
            _muxer.Pb = _io;
            _stream = _muxer.NewStream(_encoder.Codec);
            _stream.Codecpar!.CopyFrom(_encoder);
            _stream.TimeBase = _encoder.TimeBase;
            using var options = new MediaDictionary
            {
                ["plays"] = "0",
                ["final_delay"] = FormattableString.Invariant($"{finalFrameDelayMs}/1000"),
            };
            _muxer.WriteHeader(options);
            _frame = Frame.CreateVideo(width, height, AVPixelFormat.Rgba);
            _packet = new Packet();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public unsafe void AddFrame(ReadOnlySpan<byte> rgba, int delayMs, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_finished) throw new InvalidOperationException("The animation has already finished.");
        ValidateDelay(delayMs);
        if (rgba.Length != checked(_rowBytes * _height))
            throw new ArgumentException("The RGBA buffer does not match the animation dimensions.", nameof(rgba));
        cancellationToken.ThrowIfCancellationRequested();
        var nextPts = checked(_pts + delayMs);
        _frame.MakeWritable();
        for (var y = 0; y < _height; y++)
            rgba.Slice(y * _rowBytes, _rowBytes).CopyTo(new Span<byte>((byte*)_frame.Data[0] + y * _frame.Linesize[0], _rowBytes));
        _frame.Pts = _pts;
        _frame.Duration = delayMs;
        foreach (var packet in _encoder.EncodeFrame(_frame, _packet, unref: false))
            WritePacket(packet, cancellationToken);
        _pts = nextPts;
        _lastFrameDelayMs = delayMs;
        _frameCount++;
    }

    public byte[] Complete(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_finished) throw new InvalidOperationException("The animation has already finished.");
        if (_frameCount == 0) throw new InvalidOperationException("The animation has no frames.");
        if (_lastFrameDelayMs != _finalFrameDelayMs)
            throw new InvalidOperationException("The final frame delay does not match the configured delay.");
        cancellationToken.ThrowIfCancellationRequested();
        _finished = true;
        foreach (var packet in _encoder.EncodeFrame(null, _packet, unref: false))
            WritePacket(packet, cancellationToken);
        _muxer.WriteTrailer();
        _io.Flush();
        return _output.ToArray();
    }

    private unsafe void WritePacket(Packet packet, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ffmpeg.av_packet_rescale_ts(packet, _encoder.TimeBase, _stream.TimeBase);
        packet.StreamIndex = _stream.Index;
        packet.Position = -1;
        _muxer.InterleavedWritePacket(packet);
        packet.Unref();
    }

    private static void ValidateDelay(int delayMs)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(delayMs);
        var divisor = GreatestCommonDivisor(delayMs, 1000);
        if (delayMs / divisor > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(delayMs), "The frame delay cannot fit the APNG rational fields.");
    }

    private static int GreatestCommonDivisor(int left, int right)
    {
        while (right != 0) (left, right) = (right, left % right);
        return left;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _packet?.Dispose();
        _frame?.Dispose();
        _muxer?.Dispose();
        _io?.Dispose();
        _encoder?.Dispose();
        _output.Dispose();
    }
}
