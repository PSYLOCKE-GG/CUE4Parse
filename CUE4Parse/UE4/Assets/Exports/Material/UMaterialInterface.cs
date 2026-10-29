using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Readers;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;

namespace CUE4Parse.UE4.Assets.Exports.Material;

[SkipObjectRegistration]
public class UMaterialInterface : UUnrealMaterial
{
    public bool bUseMobileSpecular;
    public float MobileSpecularPower = 16.0f;
    public EMobileSpecularMask MobileSpecularMask = EMobileSpecularMask.MSM_Constant;
    public FPackageIndex? FlattenedTexture;
    public FPackageIndex? MobileBaseTexture;
    public FPackageIndex? MobileNormalTexture;
    public FPackageIndex? MobileMaskTexture;

    public FStructFallback? CachedExpressionData;
    public FMaterialTextureInfo[] TextureStreamingData = Array.Empty<FMaterialTextureInfo>();
    public List<FMaterialResource> LoadedMaterialResources = new();

    public override void Deserialize(FAssetArchive Ar, long validPos)
    {
        if(Ar.Game == GAME_WorldofJadeDynasty) Ar.Position += 24;
        base.Deserialize(Ar, validPos);
        bUseMobileSpecular = GetOrDefault<bool>(nameof(bUseMobileSpecular));
        MobileSpecularPower = GetOrDefault<float>(nameof(MobileSpecularPower));
        MobileSpecularMask = GetOrDefault<EMobileSpecularMask>(nameof(MobileSpecularMask));
        FlattenedTexture = GetOrDefault<FPackageIndex?>(nameof(FlattenedTexture));
        MobileBaseTexture = GetOrDefault<FPackageIndex?>(nameof(MobileBaseTexture));
        MobileNormalTexture = GetOrDefault<FPackageIndex?>(nameof(MobileNormalTexture));
        MobileMaskTexture = GetOrDefault<FPackageIndex?>(nameof(MobileMaskTexture));
        TextureStreamingData = GetOrDefault(nameof(TextureStreamingData), Array.Empty<FMaterialTextureInfo>());

        var bSavedCachedExpressionData = FUE5ReleaseStreamObjectVersion.Get(Ar) >= FUE5ReleaseStreamObjectVersion.Type.MaterialInterfaceSavedCachedData && Ar.ReadBoolean();
        if (bSavedCachedExpressionData)
        {
            CachedExpressionData = new FStructFallback(Ar, "MaterialCachedExpressionData");
        }

        if (Ar.Game == GAME_HogwartsLegacy) CustomGameData = new FSHAHash(Ar);
    }

    protected internal override void WriteJson(JsonWriter writer, JsonSerializer serializer)
    {
        base.WriteJson(writer, serializer);

        if (LoadedMaterialResources is not null)
        {
            writer.WritePropertyName("LoadedMaterialResources");
            serializer.Serialize(writer, LoadedMaterialResources);
        }

        if (CachedExpressionData is not null)
        {
            writer.WritePropertyName("CachedExpressionData");
            serializer.Serialize(writer, CachedExpressionData);
        }

    }

    public override void GetParams(CMaterialParams parameters)
    {
        if (FlattenedTexture != null) parameters.Diffuse = FlattenedTexture;
        if (MobileBaseTexture != null) parameters.Diffuse = MobileBaseTexture;
        if (MobileNormalTexture != null) parameters.Normal = MobileNormalTexture;
        if (MobileMaskTexture != null) parameters.Opacity = MobileMaskTexture;
        parameters.UseMobileSpecular = bUseMobileSpecular;
        parameters.MobileSpecularPower = MobileSpecularPower;
        parameters.MobileSpecularMask = MobileSpecularMask;
    }

    public override void GetParams(CMaterialParams2 parameters, EMaterialDepth depth)
    {
        if (FlattenedTexture != null)
            parameters.VerifyTexture("Diffuse", FlattenedTexture, false);
        if (MobileBaseTexture != null)
            parameters.VerifyTexture("Diffuse", MobileBaseTexture, false);
        if (MobileNormalTexture != null)
            parameters.VerifyTexture("Normal", MobileNormalTexture, false);

        for (int i = 0; i < TextureStreamingData.Length; i++)
        {
            var name = TextureStreamingData[i].TextureName.Text;
            if (!parameters.Textures.TryGetValue(name, out var texture))
                continue;

            parameters.VerifyTexture(name, texture, false);
        }

        // *****************************************
        // CachedExpressionData ONLY AFTER THIS LINE
        // *****************************************

        if (CachedExpressionData == null) return;
        _cachedParameterValues ??= CachedExpressionData.TryGetValue(out FStructFallback materialParameters, "Parameters")
            ? ReadCachedParameterValues(materialParameters, legacy: true)
            : ReadCachedParameterValues(CachedExpressionData, legacy: false);
        _cachedParameterValues.ApplyTo(parameters);
    }

    private CachedParameterValues? _cachedParameterValues;

    private sealed record CachedParameterValues(
        (string Name, float Value)[] Scalars,
        (string Name, FLinearColor Value)[] Colors,
        (string Name, ILoadableObject Value)[] Textures)
    {
        public void ApplyTo(CMaterialParams2 parameters)
        {
            foreach (var (name, value) in Scalars) parameters.Scalars[name] = value;
            foreach (var (name, value) in Colors) parameters.Colors[name] = value;
            foreach (var (name, value) in Textures) parameters.VerifyTexture(name, value);
        }
    }

    private static CachedParameterValues ReadCachedParameterValues(FStructFallback materialParameters, bool legacy)
    {
        var infosName = legacy ? "ParameterInfos" : "ParameterInfoSet";
        var textureEntry = legacy ? 2 : 3;
        var scalars = Array.Empty<(string, float)>();
        var colors = Array.Empty<(string, FLinearColor)>();
        var textures = Array.Empty<(string, ILoadableObject)>();
        if (!materialParameters.TryGetAllValues(out FStructFallback[] runtimeEntries, "RuntimeEntries"))
            return new CachedParameterValues(scalars, colors, textures);

        if (materialParameters.TryGetValue(out float[] scalarValues, "ScalarValues") &&
            runtimeEntries.Length > 0 &&
            runtimeEntries[0].TryGetValue(out FMaterialParameterInfo[] scalarParameterInfos, infosName))
            scalars = scalarParameterInfos.Select((info, i) => (info.Name.Text, scalarValues[i])).ToArray();

        if (materialParameters.TryGetValue(out FLinearColor[] vectorValues, "VectorValues") &&
            runtimeEntries.Length > 1 &&
            runtimeEntries[1].TryGetValue(out FMaterialParameterInfo[] vectorParameterInfos, infosName))
            colors = vectorParameterInfos.Select((info, i) => (info.Name.Text, vectorValues[i])).ToArray();

        if (runtimeEntries.Length > textureEntry &&
            ReadTextureValues(materialParameters, legacy) is { } textureValues &&
            runtimeEntries[textureEntry].TryGetValue(out FMaterialParameterInfo[] textureParameterInfos, infosName))
            textures = textureParameterInfos.Select((info, i) => (info.Name.Text, textureValues[i])).ToArray();

        return new CachedParameterValues(scalars, colors, textures);
    }

    private static ILoadableObject[]? ReadTextureValues(FStructFallback materialParameters, bool legacy) =>
        legacy
            ? materialParameters.TryGetValue(out FPackageIndex[] indices, "TextureValues") ? indices.Cast<ILoadableObject>().ToArray() : null
            : materialParameters.TryGetValue(out FSoftObjectPath[] paths, "TextureValues") ? paths.Cast<ILoadableObject>().ToArray() : null;

    public void DeserializeInlineShaderMaps(FAssetArchive Ar, ICollection<FMaterialResource> loadedResources)
    {
        var numLoadedResources = Ar.Read<int>();
        if (numLoadedResources > 0)
        {
            FMaterialResourceProxyReader resourceAr;
            if (Ar.Game != GAME_Stalker2)
            {
                resourceAr = new FMaterialResourceProxyReader(Ar);
            }
            else
            {
                var ShaderMaps = new FByteBulkData(Ar);
                using var ShaderMapsAr = new FByteArchive("ShaderMaps", ShaderMaps.Data, Ar.Versions);
                resourceAr = new FMaterialResourceProxyReader(ShaderMapsAr);
            }

            for (var resourceIndex = 0; resourceIndex < numLoadedResources; ++resourceIndex)
            {
                var loadedResource = new FMaterialResource();
                loadedResource.DeserializeInlineShaderMap(resourceAr);
                loadedResources.Add(loadedResource);
            }
        }
    }
}
