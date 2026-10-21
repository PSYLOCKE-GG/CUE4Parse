using CUE4Parse.UE4.Exceptions;
using CUE4Parse.UE4.Kismet;

namespace CUE4Parse.UE4.Assets.Readers;

internal sealed class FScriptBytecode
{
    private readonly FAssetArchive _source;
    private readonly long _offset;
    public int Length { get; }

    public FScriptBytecode(FAssetArchive source, int length)
    {
        _source = source;
        _offset = source.Position;
        Length = length;
    }

    public IEnumerable<KismetExpression> ReadExpressions(string name)
    {
        using var script = new FKismetArchive(name, ReadBytes(), _source.Owner!,
            _source.Versions, _source.NameMap, _source.RealmIndex);

        while (script.Position < script.Length)
        {
            yield return script.ReadExpression();
        }
    }

    private byte[] ReadBytes()
    {
        if (_offset < 0 || Length <= 0 || _offset > _source.Length - Length)
            throw new ParserException(_source, $"Script range {_offset}+{Length} exceeds the package source");

        var bytes = new byte[Length];
        var read = 0;
        while (read < bytes.Length)
        {
            var count = _source.ReadAt(_offset + read, bytes, read, bytes.Length - read);
            if (count == 0)
                throw new ParserException(_source, $"Script ended after {read} bytes; expected {Length}");
            read += count;
        }
        return bytes;
    }
}
