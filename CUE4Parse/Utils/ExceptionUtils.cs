namespace CUE4Parse.Utils;

internal static class ExceptionUtils
{
    /// <summary>
    /// Whether <paramref name="exception"/> is, or wraps, a failure of the byte source underneath a
    /// load rather than a verdict about the asset itself.
    /// </summary>
    /// <remarks>
    /// The optional-load helpers answer null for an asset the provider has not mounted, which is only
    /// a truthful answer when the bytes were actually reachable. Backing a provider with a remote or
    /// ranged-read source makes a network failure or a cancelled read indistinguishable from an
    /// absent asset unless it is let through as itself.
    /// <br/>
    /// The chain is walked because a failure raised while deserializing an export arrives wrapped in
    /// a <see cref="UE4.Exceptions.ParserException"/> naming the field it was on.
    /// </remarks>
    internal static bool IsReadFailure(this Exception exception)
    {
        for (var ex = exception; ex != null; ex = ex.InnerException)
        {
            if (ex is IOException or OperationCanceledException)
                return true;
        }

        return false;
    }
}
