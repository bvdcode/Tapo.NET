using System;
using Tapo.Internal;
using Xunit;

namespace Tapo.Tests.Unit;

public sealed class HexUtilitiesTests
{
    [Fact]
    public void Lowercase_hex_round_trip_matches_input()
    {
        var bytes = new byte[] { 0x00, 0x10, 0xAB, 0xFF };
        var hex = HexUtilities.ToHexString(bytes);
        Assert.Equal("0010abff", hex);
        Assert.Equal(bytes, HexUtilities.FromHexString(hex));
    }

    [Fact]
    public void Uppercase_lookup_table_is_used_when_requested()
    {
        var hex = HexUtilities.ToHexString(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, upperCase: true);
        Assert.Equal("DEADBEEF", hex);
    }

    [Fact]
    public void Empty_input_returns_empty_string()
    {
        Assert.Equal(string.Empty, HexUtilities.ToHexString(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Odd_length_string_throws()
    {
        Assert.Throws<FormatException>(() => HexUtilities.FromHexString("abc"));
    }
}
