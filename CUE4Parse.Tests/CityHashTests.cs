using System.Text;
using CUE4Parse.Utils;

namespace CUE4Parse.Tests;

/// <summary>
/// Locks in <see cref="CityHash.CityHash64WithSeed(byte[], ulong)"/> against UE5 <c>FHashedName</c>
/// outputs captured from a real cooked build. The engine computes
/// <c>CityHash64WithSeed(UpperUTF8Name, NameLength, FName.Number)</c>; for every shader type /
/// vertex factory class the number portion is 0, so these fixtures use seed 0.
/// </summary>
public class CityHashTests
{
    private static ulong HashUpper(string name, ulong seed = 0UL)
    {
        var bytes = Encoding.ASCII.GetBytes(name.ToUpperInvariant());
        return CityHash.CityHash64WithSeed(bytes, seed);
    }

    [Theory]
    // Vertex factory FHashedNames observed in Marvel Rivals M_Common_Hair shader maps.
    [InlineData("FLocalVertexFactory", 11475683181038621400UL)]
    [InlineData("FGPUSkinPassthroughVertexFactory", 7884826846012382956UL)]
    [InlineData("FParticleSpriteVertexFactory", 1936260693301728965UL)]
    [InlineData("FMeshParticleVertexFactory", 3257961110001812583UL)]
    [InlineData("FNiagaraSpriteVertexFactory", 13168243933419104092UL)]
    [InlineData("FNiagaraRibbonVertexFactory", 549208615835106585UL)]
    // Material shader type FHashedNames.
    [InlineData("TBasePassVSFNoLightMapPolicy", 16833942227387653686UL)]
    [InlineData("TBasePassPSFNoLightMapPolicy", 4974208445782451494UL)]
    public void CityHash64WithSeed_MatchesUE5FHashedName(string name, ulong expected)
    {
        Assert.Equal(expected, HashUpper(name));
    }
}
