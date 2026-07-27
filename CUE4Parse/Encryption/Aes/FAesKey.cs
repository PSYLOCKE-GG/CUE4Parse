using AesProvider = System.Security.Cryptography.Aes;

namespace CUE4Parse.Encryption.Aes;

public class FAesKey
{
    public readonly byte[] Key;
    public string KeyString => "0x"+Convert.ToHexString(Key);
    public bool IsDefault => Key.All(x => x == 0);

    private AesProvider? _provider;

    // Keyed AES instance for the one-shot span APIs. Safe for concurrent use because the
    // key is set once and never mutated afterwards.
    internal AesProvider Provider
    {
        get
        {
            if (_provider == null)
            {
                var aes = AesProvider.Create();
                aes.Key = Key;
                if (Interlocked.CompareExchange(ref _provider, aes, null) != null) aes.Dispose();
            }
            return _provider;
        }
    }

    public FAesKey(byte[] key, bool ignoreLength = false)
    {
        if (!ignoreLength && key.Length != 32)
            throw new ArgumentException("Aes Key must be 32 bytes long");
        Key = key;
    }

    public FAesKey(string keyString)
    {
        if (keyString.StartsWith("0x") && keyString.Length == 66) Key = Convert.FromHexString(keyString.AsSpan(2));
        else if (keyString.Length == 64) Key = Convert.FromHexString(keyString);
        else throw new ArgumentException("Aes Key must be 32 bytes long");
    }

    public override string ToString() => KeyString;
}
