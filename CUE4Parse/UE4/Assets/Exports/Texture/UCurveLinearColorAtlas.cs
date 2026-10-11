namespace CUE4Parse.UE4.Assets.Exports.Texture;

/// <summary>
/// A texture of gradient rows. Its constructor clamps, filters bilinearly and stores linear colour
/// (CurveLinearColorAtlas.cpp: UCurveLinearColorAtlas::UCurveLinearColorAtlas), so a cooked atlas omits those properties.
/// </summary>
public class UCurveLinearColorAtlas : UTexture2D
{
    protected override TextureAddress DefaultAddress => TextureAddress.TA_Clamp;
    protected override TextureFilter DefaultFilter => TextureFilter.TF_Bilinear;
    protected override bool DefaultSRGB => false;
}
