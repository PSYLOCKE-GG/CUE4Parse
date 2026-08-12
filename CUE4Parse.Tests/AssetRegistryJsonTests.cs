using System.Runtime.CompilerServices;
using CUE4Parse.UE4.AssetRegistry.Objects;
using Newtonsoft.Json;

namespace CUE4Parse.Tests;

public class AssetRegistryJsonTests
{
    [Fact]
    public void PackageDataJsonIncludesParsedExtension()
    {
        var package = (FAssetPackageData) RuntimeHelpers.GetUninitializedObject(typeof(FAssetPackageData));
        typeof(FAssetPackageData).GetField(nameof(FAssetPackageData.ExtensionText))!
            .SetValue(package, ".uasset");

        var json = JsonConvert.SerializeObject(package);

        Assert.Contains("\"ExtensionText\":\".uasset\"", json);
    }
}
