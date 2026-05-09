using System;
using Tapo.Cryptography;
using Xunit;

namespace Tapo.Tests.Unit;

public sealed class KeyExchangeTests
{
    [Fact]
    public void Parses_username_and_nonce_from_header()
    {
        var exchange = KeyExchange.Parse("username=\"admin\" nonce=\"deadbeef\"");
        Assert.Equal("admin", exchange.Username);
        Assert.Equal("deadbeef", exchange.Nonce);
    }

    [Fact]
    public void Throws_when_required_fields_are_missing()
    {
        Assert.Throws<FormatException>(() => KeyExchange.Parse("username=\"admin\""));
    }
}
