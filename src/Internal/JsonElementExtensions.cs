using System.Text.Json;

namespace Tapo.Internal;

/// <summary>
/// Tiny helpers that turn missing fields into a meaningful exception instead
/// of <see cref="System.Collections.Generic.KeyNotFoundException"/>.
/// </summary>
internal static class JsonElementExtensions
{
    public static JsonElement GetPropertyOrThrow(this JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            throw new TapoProtocolException($"Camera response is missing the '{name}' property.");
        }

        return value;
    }
}
