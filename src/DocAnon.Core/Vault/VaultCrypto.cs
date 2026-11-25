using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace DocAnon.Core.Vault;

/// <summary>How the file key is protected.</summary>
public abstract record VaultKey
{
    private VaultKey() { }

    /// <summary>The key is derived from a password with PBKDF2-SHA256.</summary>
    public sealed record Password(string Value) : VaultKey;

    /// <summary>A random key protected by Windows DPAPI for the current user.</summary>
    public sealed record WindowsUser : VaultKey;
}

/// <summary>
/// AES-256-GCM file encryption.
/// Layout: magic "DOCANON1" | mode (1 password, 2 DPAPI) | key material | nonce(12) | tag(16) | ciphertext.
/// For mode 1 the key material is salt(16) + iterations(int32). For mode 2 it is the DPAPI blob
/// length (int32) followed by the blob.
/// </summary>
public static class VaultCrypto
{
    private static readonly byte[] Magic = "DOCANON1"u8.ToArray();
    public const int Pbkdf2Iterations = 600_000;
    private const int KeySize = 32, NonceSize = 12, TagSize = 16, SaltSize = 16;

    public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, VaultKey key)
    {
        using var ms = new MemoryStream();
        ms.Write(Magic);
        byte[] fileKey;
        switch (key)
        {
            case VaultKey.Password p:
            {
                ms.WriteByte(1);
                byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
                ms.Write(salt);
                Span<byte> it = stackalloc byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(it, Pbkdf2Iterations);
                ms.Write(it);
                fileKey = Derive(p.Value, salt, Pbkdf2Iterations);
                break;
            }
            case VaultKey.WindowsUser:
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("A proteção pelo usuário do Windows só existe no Windows.");
                ms.WriteByte(2);
                fileKey = RandomNumberGenerator.GetBytes(KeySize);
                byte[] blob = ProtectedData.Protect(fileKey, Magic, DataProtectionScope.CurrentUser);
                Span<byte> len = stackalloc byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(len, blob.Length);
                ms.Write(len);
                ms.Write(blob);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(key));
        }

        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] tag = new byte[TagSize];
        byte[] cipher = new byte[plaintext.Length];
        byte[] header = ms.ToArray();
        using (var gcm = new AesGcm(fileKey, TagSize))
        {
            // The header is authenticated, so the mode and salt cannot be swapped.
            gcm.Encrypt(nonce, plaintext, cipher, tag, header);
        }
        CryptographicOperations.ZeroMemory(fileKey);
        ms.Write(nonce);
        ms.Write(tag);
        ms.Write(cipher);
        return ms.ToArray();
    }

    /// <summary>Decrypts a vault file. A wrong password raises <see cref="AuthenticationTagMismatchException"/>.</summary>
    public static byte[] Decrypt(ReadOnlySpan<byte> data, VaultKey key)
    {
        if (data.Length < Magic.Length + 1 || !data[..Magic.Length].SequenceEqual(Magic))
            throw new InvalidDataException("O arquivo não é um cofre do DocAnon.");
        int pos = Magic.Length;
        byte mode = data[pos++];
        byte[] fileKey;
        if (mode == 1)
        {
            if (key is not VaultKey.Password p) throw new InvalidOperationException("Este cofre é protegido por senha.");
            var salt = data.Slice(pos, SaltSize).ToArray();
            pos += SaltSize;
            int iterations = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos, 4));
            pos += 4;
            fileKey = Derive(p.Value, salt, iterations);
        }
        else if (mode == 2)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Este cofre foi protegido pelo usuário do Windows.");
            int len = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos, 4));
            pos += 4;
            var blob = data.Slice(pos, len).ToArray();
            pos += len;
            fileKey = ProtectedData.Unprotect(blob, Magic, DataProtectionScope.CurrentUser);
        }
        else throw new InvalidDataException("Modo de proteção desconhecido.");

        var header = data[..pos];
        var nonce = data.Slice(pos, NonceSize);
        pos += NonceSize;
        var tag = data.Slice(pos, TagSize);
        pos += TagSize;
        var cipher = data[pos..];
        byte[] plain = new byte[cipher.Length];
        try
        {
            using var gcm = new AesGcm(fileKey, TagSize);
            gcm.Decrypt(nonce, cipher, tag, plain, header);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(fileKey);
        }
        return plain;
    }

    public static bool IsPasswordProtected(ReadOnlySpan<byte> data) => data.Length > Magic.Length && data[Magic.Length] == 1;

    private static byte[] Derive(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, KeySize);
}
