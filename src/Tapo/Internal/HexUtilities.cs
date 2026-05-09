using System;
using System.Globalization;

namespace Tapo.Internal;

/// <summary>
/// Hex encoding helpers. .NET Standard 2.1 lacks <c>Convert.ToHexString</c>.
/// </summary>
internal static class HexUtilities
{
    private static readonly char[] LowerLookup = "0123456789abcdef".ToCharArray();
    private static readonly char[] UpperLookup = "0123456789ABCDEF".ToCharArray();

    public static string ToHexString(ReadOnlySpan<byte> bytes, bool upperCase = false)
    {
        if (bytes.IsEmpty)
        {
            return string.Empty;
        }

        var lookup = upperCase ? UpperLookup : LowerLookup;
        Span<char> chars = bytes.Length <= 256
            ? stackalloc char[bytes.Length * 2]
            : new char[bytes.Length * 2];

        for (var i = 0; i < bytes.Length; i++)
        {
            var b = bytes[i];
            chars[(i * 2) + 0] = lookup[b >> 4];
            chars[(i * 2) + 1] = lookup[b & 0x0F];
        }

        return new string(chars);
    }

    public static byte[] FromHexString(string hex)
    {
        if (hex is null)
        {
            throw new ArgumentNullException(nameof(hex));
        }

        if ((hex.Length & 1) != 0)
        {
            throw new FormatException("Hex string must have an even length.");
        }

        var result = new byte[hex.Length / 2];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = byte.Parse(
                hex.AsSpan(i * 2, 2),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture);
        }

        return result;
    }
}
