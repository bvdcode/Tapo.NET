using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Tapo.Control;

/// <summary>
/// Authenticated control-channel session. Implementations transparently
/// re-authenticate when <c>stok</c> expires.
/// </summary>
public interface IControlChannel : IAsyncDisposable
{
    /// <summary>True after a successful login.</summary>
    bool IsAuthenticated { get; }

    /// <summary>The negotiated password hashing algorithm. Required for media-stream key derivation.</summary>
    EncryptionMethod EncryptionMethod { get; }

    /// <summary>Performs login if necessary.</summary>
    Task AuthenticateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a single API request and returns the parsed JSON document. The
    /// returned document is owned by the caller — dispose it once done.
    /// </summary>
    Task<JsonDocument> SendAsync(JsonElement request, CancellationToken cancellationToken = default);
}
