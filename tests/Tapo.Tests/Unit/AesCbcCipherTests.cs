using System;
using System.Linq;
using System.Text;
using Tapo.Cryptography;
using Xunit;

namespace Tapo.Tests.Unit;

public sealed class AesCbcCipherTests
{
    [Fact]
    public void Round_trips_arbitrary_payloads()
    {
        var key = new byte[16];
        var iv = new byte[16];
        for (var i = 0; i < 16; i++)
        {
            key[i] = (byte)i;
            iv[i] = (byte)(i * 2);
        }

        using var cipher = new AesCbcCipher(key, iv);
        foreach (var payload in new[] { "", "x", "the quick brown fox jumps over the lazy dog" })
        {
            var bytes = Encoding.UTF8.GetBytes(payload);
            var encrypted = cipher.Encrypt(bytes);
            var decrypted = cipher.Decrypt(encrypted);

            Assert.Equal(bytes, decrypted);
        }
    }

    [Fact]
    public void Throws_for_keys_of_unsupported_length()
    {
        Assert.Throws<ArgumentException>(() => new AesCbcCipher(new byte[15], new byte[16]));
        Assert.Throws<ArgumentException>(() => new AesCbcCipher(new byte[16], new byte[15]));
    }

    [Fact]
    public void From_key_exchange_disables_encryption_when_username_is_none()
    {
        var exchange = KeyExchange.Parse("username=\"none\" nonce=\"deadbeefdeadbeef\"");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            AesCbcCipher.FromKeyExchange(exchange, "anything", string.Empty, EncryptionMethod.Md5));
        Assert.Contains("super-secret", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
