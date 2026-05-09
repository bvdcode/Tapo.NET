using System.Collections.Generic;

namespace Tapo.Control;

/// <summary>
/// Tapo API error codes — minimal subset for diagnostic messages. We ship
/// only the values that the client surfaces in exceptions; the full table
/// is firmware-specific and grows between Tapo releases.
/// </summary>
internal static class ErrorCodes
{
    private static readonly Dictionary<int, string> Names = new()
    {
        [-40401] = "INVALID_STOK",
        [-40209] = "INVALID_LOGIN_CREDENTIALS",
        [-40411] = "INVALID_AUTHENTICATION_DATA",
        [-40413] = "INVALID_NONCE",
        [-40414] = "NEED_LOGIN_BY_LOCAL_PASSWORD",
        [-40418] = "TPAP_AUTHENTICATION_FAILED",
        [-71101] = "USER_ID_FULL",
        [-71102] = "USER_ID_EMPLOYED",
        [-71103] = "USER_ID_INVALID",
        [-71105] = "PLAYBACK_SEARCH_FAILED",
        [-52409] = "SD_CARD_UNPLUGGED",
        [-52405] = "TOO_MANY_REQUEST",
        [-52407] = "TOO_MANY_CLIENT",
        [-52419] = "TOO_MANY_HTTPS_CLIENT",
        [-52402] = "VOD_INVALID_REQUEST",
    };

    public static string? TryGetName(int code) => Names.TryGetValue(code, out var name) ? name : null;
}
