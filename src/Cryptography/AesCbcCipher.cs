using System;
using System.Security.Cryptography;
using System.Text;

namespace Tapo.Cryptography;

/// <summary>
/// AES-128-CBC with PKCS7 padding. Each call uses a freshly created cipher
/// instance with the original IV — matching the Python reference, where the
/// IV state is reset before every encrypt/decrypt operation.
/// </summary>
public sealed class AesCbcCipher : ITapoCipher, IDisposable
{
    private readonly Aes _aes;

    public AesCbcCipher(byte[] key, byte[] iv)
    {
        if (key is null) throw new ArgumentNullException(nameof(key));
        if (iv is null) throw new ArgumentNullException(nameof(iv));
        if (key.Length != 16) throw new ArgumentException("Tapo AES key must be 16 bytes.", nameof(key));
        if (iv.Length != 16) throw new ArgumentException("Tapo AES IV must be 16 bytes.", nameof(iv));

        _aes = Aes.Create();
        _aes.Mode = CipherMode.CBC;
        _aes.Padding = PaddingMode.PKCS7;
        _aes.KeySize = 128;
        _aes.Key = key;
        _aes.IV = iv;
    }

    /// <summary>
    /// Derives the cipher from the camera's <c>Key-Exchange</c> handshake.
    /// </summary>
    /// <param name="exchange">Parsed Key-Exchange header.</param>
    /// <param name="cloudPassword">Cloud password used by the camera for media encryption.</param>
    /// <param name="superSecretKey">Fallback secret used when media encryption is disabled (<c>username</c> equals "none"). May be empty.</param>
    /// <param name="method">Hash algorithm advertised by the camera.</param>
    public static AesCbcCipher FromKeyExchange(
        KeyExchange exchange,
        string cloudPassword,
        string superSecretKey,
        EncryptionMethod method)
    {
        if (cloudPassword is null) throw new ArgumentNullException(nameof(cloudPassword));
        if (superSecretKey is null) throw new ArgumentNullException(nameof(superSecretKey));

        var nonceBytes = Encoding.ASCII.GetBytes(exchange.Nonce);
        var usernameBytes = Encoding.ASCII.GetBytes(exchange.Username);

        byte[] key;
        if (string.Equals(exchange.Username, "none", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(superSecretKey))
            {
                throw new InvalidOperationException(
                    "Camera reports media encryption disabled but no super-secret key was provided.");
            }

            key = Md5(Concat(nonceBytes, Colon, Encoding.UTF8.GetBytes(superSecretKey)));
        }
        else
        {
            var hashedPassword = PasswordHasher.HashPassword(cloudPassword, method);
            key = Md5(Concat(nonceBytes, Colon, hashedPassword));
        }

        var iv = Md5(Concat(usernameBytes, Colon, nonceBytes));
        return new AesCbcCipher(key, iv);
    }

    public byte[] Encrypt(ReadOnlySpan<byte> plaintext)
    {
        using var encryptor = _aes.CreateEncryptor();
        var src = plaintext.ToArray();
        return encryptor.TransformFinalBlock(src, 0, src.Length);
    }

    public byte[] Decrypt(ReadOnlySpan<byte> ciphertext)
    {
        using var decryptor = _aes.CreateDecryptor();
        var src = ciphertext.ToArray();
        return decryptor.TransformFinalBlock(src, 0, src.Length);
    }

    public void Dispose() => _aes.Dispose();

    private static readonly byte[] Colon = { (byte)':' };

    private static byte[] Md5(byte[] data)
    {
        using var md5 = MD5.Create();
        return md5.ComputeHash(data);
    }

    private static byte[] Concat(byte[] a, byte[] b, byte[] c)
    {
        var result = new byte[a.Length + b.Length + c.Length];
        Buffer.BlockCopy(a, 0, result, 0, a.Length);
        Buffer.BlockCopy(b, 0, result, a.Length, b.Length);
        Buffer.BlockCopy(c, 0, result, a.Length + b.Length, c.Length);
        return result;
    }
}
