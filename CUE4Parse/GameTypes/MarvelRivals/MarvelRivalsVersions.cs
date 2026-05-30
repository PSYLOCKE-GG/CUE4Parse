using CUE4Parse.Utils;

namespace CUE4Parse.GameTypes.MarvelRivals;

/// <summary>
/// Known Marvel Rivals build numbers at which on-disk serialization changed.
/// Names describe what each build introduced, so call sites read as "since &lt;what&gt;".
/// </summary>
public static class MarvelRivalsVersions
{
    /// <summary>First build that appends an <c>FGameplayTagContainer</c> after each <c>FSkeletalMaterial</c>.</summary>
    public static readonly ArbitraryVersion SkeletalMaterialGameplayTags = "1.1.1573788";

    /// <summary>First build that appends a trailing <c>int32</c> after each <c>FStringTable</c> entry value.</summary>
    public static readonly ArbitraryVersion StringTableEntryTrailingInt = "1.1.1933977";

    /// <summary>Build where the trailing <c>FStringTable</c> entry field widens from <c>int32</c> to <c>FString</c>.</summary>
    public static readonly ArbitraryVersion StringTableEntryTrailingFString = "1.1.3006564";
}
