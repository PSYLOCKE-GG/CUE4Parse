using System.Diagnostics.CodeAnalysis;
using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Vfs;
using CUE4Parse.GameTypes.DFHO.Assets.Objects;
using CUE4Parse.UE4.Assets.Readers;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;

namespace CUE4Parse.UE4.Assets.Exports.Internationalization;

public class UStringTable : UObject
{
    public FStringTable StringTable { get; private set; }

    /// <summary>
    /// Loads a string table into the shared owner. A table is shared content, and the shared
    /// owner's store is its only cache, so a repeat lookup reuses the resident package.
    /// </summary>
    internal static bool TryGet(IFileProvider provider, string tableId, [MaybeNullWhen(false)] out UStringTable table)
    {
        if (provider is not AbstractFileProvider owners)
            return provider.TryLoadPackageObject(tableId, out table);

        using var _ = owners.EnterOwner(owners.SharedOwner);
        return provider.TryLoadPackageObject(tableId, out table);
    }

    public override void Deserialize(FAssetArchive Ar, long validPos)
    {
        base.Deserialize(Ar, validPos);

        StringTable = new FStringTable(Ar);
        if (Ar.Game is GAME_DeltaForce && StringTable.KeysToEntries.Count == 0 &&
            Ar.Owner?.Provider is IVfsFileProvider provider && provider.TryCreateReader(Path.ChangeExtension(Ar.Name, "ustbin"), out var reader))
        {
            var deltaStringTable = new FDeltaStringTable(reader);
            StringTable.TableNamespace = deltaStringTable.TableNamespace;
            StringTable.KeysToEntries = deltaStringTable.KeysToEntries.ToDictionary(pair => pair.Key, pair => pair.Value.Name);
            Ar.Position += 8;
        }
    }

    protected internal override void WriteJson(JsonWriter writer, JsonSerializer serializer)
    {
        base.WriteJson(writer, serializer);

        writer.WritePropertyName("StringTable");
        serializer.Serialize(writer, StringTable);
    }
}
