namespace CUE4Parse.UE4.Assets;

/// <summary>
/// Per-load fidelity flags, passed at LoadPackage time and fixed for the lifetime of the
/// loaded package: every export deserialized from that package instance observes them.
/// Loads with different flags are distinct packages (and distinct PackageCache entries).
/// </summary>
[Flags]
public enum EPackageReadFlags
{
    None = 0,

    /// <summary>
    /// UAnimSequence exports deserialize metadata only: SequenceLength, notifies and float
    /// curves are populated, but the compressed bone stream is neither read nor decoded and
    /// BoneCompressionSettings is never resolved (UE4.25+ compressed-data format; older
    /// formats deserialize in full). Curve compression settings are resolved on demand, so
    /// sequences without curves resolve no imports at all. Affected instances are marked
    /// IsBoneDataStripped and accessing their CompressedDataStructure throws.
    /// </summary>
    AnimMetadataOnly = 1 << 0,
}
