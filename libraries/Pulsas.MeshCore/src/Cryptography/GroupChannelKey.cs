using System.Security.Cryptography;
using System.Text;

namespace Pulsas.MeshCore.Cryptography;

public class GroupChannelKey : IDisposable
{
    private readonly byte[] secret;
    private bool disposed;

    public GroupChannelKey(ReadOnlySpan<byte> key)
    {
        if (key.Length is not (16 or 32))
        {
            throw new ArgumentException("Channel secrets must be 16 or 32 bytes.", nameof(key));
        }

        secret = new byte[32];
        key.CopyTo(secret);
        ChannelHash = SHA256.HashData(key)[0];
    }

    public byte ChannelHash { get; }

    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    public static GroupChannelKey StandardPublic() =>
        new(Convert.FromBase64String("izOH6cXN6mrJ5e26oRXNcg=="));

    public static GroupChannelKey FromHashtag(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length < 2 || name[0] != '#')
        {
            throw new ArgumentException(
                "A hashtag channel name must start with # and contain a name.",
                nameof(name)
            );
        }

        return new(SHA256.HashData(Encoding.UTF8.GetBytes(name)).AsSpan(0, 16));
    }

    internal byte[] Mac(ReadOnlySpan<byte> ciphertext)
    {
        ThrowIfDisposed();
        return HMACSHA256.HashData(secret, ciphertext);
    }

    internal byte[] Transform(ReadOnlySpan<byte> input, bool encrypt)
    {
        ThrowIfDisposed();
        using var aes = Aes.Create();
        var cipherKey = secret.AsSpan(0, 16).ToArray();
        try
        {
            aes.Key = cipherKey;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(cipherKey);
        }
        return encrypt
            ? aes.EncryptEcb(input, PaddingMode.None)
            : aes.DecryptEcb(input, PaddingMode.None);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(secret);
        disposed = true;
        GC.SuppressFinalize(this);
    }
}
