using CUE4Parse.UE4.Assets.Readers;
using CUE4Parse.UE4.Objects.GameplayTags;
using CUE4Parse.UE4.Objects.Meshes;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Versions;
using CUE4Parse.Utils;
using Newtonsoft.Json;

namespace CUE4Parse.UE4.Assets.Exports.SkeletalMesh;

[JsonConverter(typeof(FSkeletalMaterialConverter))]
public class FSkeletalMaterial
{
    public FPackageIndex MaterialIndex; // raw import/export reference
    public ResolvedObject? Material; // UMaterialInterface
    public FName MaterialSlotName;
    public FName? ImportedMaterialSlotName;
    public FMeshUVChannelInfo? UVChannelData;
    public FPackageIndex OverlayMaterialInterface;

    public FSkeletalMaterial(FAssetArchive Ar)
    {
        MaterialIndex = new FPackageIndex(Ar);
        Material = MaterialIndex.ResolvedObject;
        if (FEditorObjectVersion.Get(Ar) >= FEditorObjectVersion.Type.RefactorMeshEditorMaterials)
        {
            MaterialSlotName = Ar.ReadFName();
            var bSerializeImportedMaterialSlotName = !Ar.Owner.HasFlags(EPackageFlags.PKG_FilterEditorOnly);
            if (FCoreObjectVersion.Get(Ar) >= FCoreObjectVersion.Type.SkeletalMaterialEditorDataStripping)
            {
                bSerializeImportedMaterialSlotName = Ar.ReadBoolean();
            }

            if (bSerializeImportedMaterialSlotName)
            {
                ImportedMaterialSlotName = Ar.ReadFName();
            }
        }
        else
        {
            if (Ar.Ver >= EUnrealEngineObjectUE4Version.MOVE_SKELETALMESH_SHADOWCASTING)
                Ar.Position += 4;

            if (FRecomputeTangentCustomVersion.Get(Ar) >= FRecomputeTangentCustomVersion.Type.RuntimeRecomputeTangent)
            {
                var bRecomputeTangent = Ar.ReadBoolean();
            }
        }
        if (FRenderingObjectVersion.Get(Ar) >= FRenderingObjectVersion.Type.TextureStreamingMeshUVChannelData)
            UVChannelData = new FMeshUVChannelInfo(Ar);

        if (FFortniteMainBranchObjectVersion.Get(Ar) >= FFortniteMainBranchObjectVersion.Type.MeshMaterialSlotOverlayMaterialAdded)
            OverlayMaterialInterface = new FPackageIndex(Ar);

        switch (Ar.Game)
        {
            case EGame.GAME_MarvelRivals:
                if (Ar.Versions.ArbitraryVersion != null && Ar.Versions.ArbitraryVersion < new ArbitraryVersion("1.1.1573788")) break;
                _ = new FGameplayTagContainer(Ar);
                break;
            case EGame.GAME_FragPunk or EGame.GAME_DaysGone or EGame.GAME_WorldofJadeDynasty or EGame.GAME_AssaultFireFuture:
                Ar.Position += 4;
                break;
            case EGame.GAME_Strinova:
                Ar.Position += 8;
                break;
        }
    }
}
