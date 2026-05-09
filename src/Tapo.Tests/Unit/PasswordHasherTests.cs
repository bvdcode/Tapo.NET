using System.Text;
using Tapo;
using Tapo.Cryptography;
using Xunit;

namespace Tapo.Tests.Unit;

public sealed class PasswordHasherTests
{
    [Fact]
    public void Md5_hex_of_admin_matches_known_vector()
    {
        // The camera firmware expects UPPER(HEX(MD5(password))).
        var hex = PasswordHasher.HashPasswordHex("admin", EncryptionMethod.Md5);
        Assert.Equal("21232F297A57A5A743894A0E4A801FC3", hex);
    }

    [Fact]
    public void Sha256_hex_matches_known_vector()
    {
        var hex = PasswordHasher.HashPasswordHex("admin", EncryptionMethod.Sha256);
        Assert.Equal("8C6976E5B5410415BDE908BD4DEE15DFB167A9C873FC4BB8A81F6F2AB448A918", hex);
    }

    [Fact]
    public void Hash_password_returns_ascii_uppercase_hex_bytes()
    {
        var bytes = PasswordHasher.HashPassword("a", EncryptionMethod.Md5);
        Assert.Equal("0CC175B9C0F1B6A831C399E269772661", Encoding.ASCII.GetString(bytes));
    }
}
