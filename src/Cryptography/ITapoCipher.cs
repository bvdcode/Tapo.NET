using System;

namespace Tapo.Cryptography;

/// <summary>
/// A symmetric cipher used to wrap individual media-stream messages. The
/// implementation must be safe to call repeatedly across many small payloads:
/// it should not leak per-message state.
/// </summary>
public interface ITapoCipher
{
    /// <summary>Encrypts <paramref name="plaintext"/> using a fresh IV state.</summary>
    byte[] Encrypt(ReadOnlySpan<byte> plaintext);

    /// <summary>Decrypts <paramref name="ciphertext"/> using a fresh IV state.</summary>
    byte[] Decrypt(ReadOnlySpan<byte> ciphertext);
}
