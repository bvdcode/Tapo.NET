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

    /// <summary>
    /// Initializes the cipher with the given key and IV. Both must be exactly 16 bytes.
    /// </summary>
    /// <param name="key">AES key.</param>
    /// <param name="iv">AES initialization vector.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="key"/> or <paramref name="iv"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown if <paramref name="key"/> or <paramref name="iv"/> is not 16 bytes long.</exception>
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

    /// <summary>
    /// Encrypts the given plaintext using the AES-128-CBC algorithm.
    /// </summary>
    /// <param name="plaintext">The plaintext to encrypt.</param>
    /// <returns>The encrypted ciphertext.</returns>
    public byte[] Encrypt(ReadOnlySpan<byte> plaintext)
    {
        using var encryptor = _aes.CreateEncryptor();
        var src = plaintext.ToArray();
        return encryptor.TransformFinalBlock(src, 0, src.Length);
    }

    /// <summary>
    /// Decrypts the given ciphertext using the AES-128-CBC algorithm.
    /// </summary>
    /// <param name="ciphertext">The ciphertext to decrypt.</param>
    /// <returns>The decrypted plaintext.</returns>
    public byte[] Decrypt(ReadOnlySpan<byte> ciphertext)
    {
        using var decryptor = _aes.CreateDecryptor();
        var src = ciphertext.ToArray();
        return decryptor.TransformFinalBlock(src, 0, src.Length);
    }

    /// <summary>
    /// Releases all resources used by the cipher. After calling this method, the cipher instance should not be used anymore.
    /// </summary>
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
