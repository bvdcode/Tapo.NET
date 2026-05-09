using System;
using Tapo.Authentication;
using Xunit;

namespace Tapo.Tests.Unit;

public sealed class NonceGeneratorTests
{
    [Theory]
    [InlineData(8)]
    [InlineData(24)]
    [InlineData(64)]
    public void Generates_hex_string_twice_the_byte_length(int bytes)
    {
        var nonce = NonceGenerator.Generate(bytes);
        Assert.Equal(bytes * 2, nonce.Length);
        foreach (var c in nonce)
        {
            Assert.Contains(c, "0123456789abcdef");
        }
    }

    [Fact]
    public void Two_calls_return_different_values()
    {
        var first = NonceGenerator.Generate(8);
        var second = NonceGenerator.Generate(8);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Throws_for_non_positive_lengths()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NonceGenerator.Generate(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => NonceGenerator.Generate(-1));
    }
}
