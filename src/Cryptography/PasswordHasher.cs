using System;
using System.Security.Cryptography;
using System.Text;
using Tapo.Internal;

namespace Tapo.Cryptography;

/// <summary>
/// Computes the upper-cased hex password digest exactly as the Python reference
/// implementation does: <c>UPPER(HEX(HASH(password)))</c>.
/// </summary>
public static class PasswordHasher
{
    /// <summary>
    /// Returns the hashed password as ASCII-encoded uppercase hexadecimal bytes —
    /// the form the camera expects in both Digest and AES key derivation.
    /// </summary>
    public static byte[] HashPassword(string password, EncryptionMethod method)
    {
        Throw.IfNull(password);

        var passwordBytes = Encoding.UTF8.GetBytes(password);
        return HashPassword(passwordBytes, method);
    }

    /// <inheritdoc cref="HashPassword(string, EncryptionMethod)"/>
    public static byte[] HashPassword(ReadOnlySpan<byte> password, EncryptionMethod method)
    {
        var digest = method switch
        {
            EncryptionMethod.Md5 => Md5(password),
            EncryptionMethod.Sha256 => Sha256(password),
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unsupported hashing method."),
        };

        return Encoding.ASCII.GetBytes(HexUtilities.ToHexString(digest, upperCase: true));
    }

    /// <summary>
    /// Returns the hashed password as an upper-cased hex string. Convenience
    /// overload for callers that need it in <see cref="string"/> form.
    /// </summary>
    public static string HashPasswordHex(string password, EncryptionMethod method)
    {
        var bytes = HashPassword(password, method);
        return Encoding.ASCII.GetString(bytes);
    }

    private static byte[] Md5(ReadOnlySpan<byte> data)
    {
        using var md5 = MD5.Create();
        return md5.ComputeHash(data.ToArray());
    }

    private static byte[] Sha256(ReadOnlySpan<byte> data)
    {
        using var sha = SHA256.Create();
        return sha.ComputeHash(data.ToArray());
    }
}
