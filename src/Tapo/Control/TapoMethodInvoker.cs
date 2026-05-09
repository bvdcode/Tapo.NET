using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Tapo.Internal;

namespace Tapo.Control;

/// <summary>
/// Thin wrapper around <see cref="IControlChannel"/> that hides the
/// <c>multipleRequest</c> envelope: every API call is sent inside a single-
/// element batch and the inner response is unwrapped before being handed
/// back to the caller. Higher-level clients (device, motion, privacy, …)
/// compose this one helper instead of poking the JSON envelope themselves.
/// </summary>
public sealed class TapoMethodInvoker
{
    private readonly IControlChannel _channel;

    /// <summary>Creates a new invoker bound to <paramref name="channel"/>.</summary>
    public TapoMethodInvoker(IControlChannel channel)
    {
        Throw.IfNull(channel);
        _channel = channel;
    }

    /// <summary>
    /// Sends a single API call and returns the inner <c>result</c> object as a
    /// detached <see cref="JsonDocument"/>. The caller owns the returned
    /// document and must dispose it.
    /// </summary>
    /// <param name="method">Tapo API method name, e.g. <c>getDeviceInfo</c>.</param>
    /// <param name="parameters">Method parameters object, or <see langword="null"/> for parameter-less calls.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<JsonDocument> InvokeAsync(string method, object? parameters, CancellationToken cancellationToken = default)
    {
        Throw.IfNullOrEmpty(method);

        using var envelope = BuildEnvelope(method, parameters);
        using var response = await _channel.SendAsync(envelope.RootElement, cancellationToken).ConfigureAwait(false);

        var first = ExtractFirstResponse(response, method);
        if (!first.TryGetProperty("result", out var result))
        {
            // Some endpoints (set*) reply with no result — return an empty object
            // for consistency rather than forcing every caller to null-check.
            return JsonDocument.Parse("{}");
        }

        return CloneToDocument(result);
    }

    /// <summary>
    /// Convenience overload that returns the inner <c>result</c> as a typed
    /// object via <see cref="JsonSerializer"/>.
    /// </summary>
    public async Task<T?> InvokeAsync<T>(string method, object? parameters, CancellationToken cancellationToken = default)
    {
        using var doc = await InvokeAsync(method, parameters, cancellationToken).ConfigureAwait(false);
        return doc.RootElement.Deserialize<T>();
    }

    /// <summary>
    /// Sends a "fire-and-forget" call that the caller does not need a response
    /// for. The server-side error code is still validated.
    /// </summary>
    public async Task ExecuteAsync(string method, object? parameters, CancellationToken cancellationToken = default)
    {
        using var _ = await InvokeAsync(method, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends multiple API methods in a single round-trip. Returns the
    /// per-method results in the same order as <paramref name="methods"/>;
    /// entries that the camera reported a non-zero error for are returned as
    /// <see langword="null"/>.
    /// </summary>
    public async Task<IReadOnlyList<JsonDocument?>> InvokeBatchAsync(
        IReadOnlyList<TapoMethodCall> methods,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(methods);
        if (methods.Count == 0) return Array.Empty<JsonDocument?>();

        using var envelope = BuildBatchEnvelope(methods);
        using var response = await _channel.SendAsync(envelope.RootElement, cancellationToken).ConfigureAwait(false);

        if (!response.RootElement.TryGetProperty("result", out var resultElem) ||
            !resultElem.TryGetProperty("responses", out var responsesElem) ||
            responsesElem.ValueKind != JsonValueKind.Array)
        {
            throw new TapoProtocolException("multipleRequest response is missing the responses array.");
        }

        var output = new JsonDocument?[methods.Count];
        var index = 0;
        foreach (var entry in responsesElem.EnumerateArray())
        {
            if (index >= methods.Count) break;
            if (entry.TryGetProperty("error_code", out var errCode) && errCode.GetInt32() != 0)
            {
                output[index] = null;
            }
            else if (entry.TryGetProperty("result", out var entryResult))
            {
                output[index] = CloneToDocument(entryResult);
            }
            else
            {
                output[index] = JsonDocument.Parse("{}");
            }

            index++;
        }

        return output;
    }

    private static JsonDocument BuildEnvelope(string method, object? parameters)
    {
        var inner = parameters is null
            ? (object)new { method }
            : new { method, @params = parameters };

        return JsonSerializer.SerializeToDocument(new
        {
            method = "multipleRequest",
            @params = new { requests = new[] { inner } },
        });
    }

    private static JsonDocument BuildBatchEnvelope(IReadOnlyList<TapoMethodCall> methods)
    {
        var requests = new object[methods.Count];
        for (var i = 0; i < methods.Count; i++)
        {
            var call = methods[i];
            requests[i] = call.Parameters is null
                ? (object)new { method = call.Method }
                : new { method = call.Method, @params = call.Parameters };
        }

        return JsonSerializer.SerializeToDocument(new
        {
            method = "multipleRequest",
            @params = new { requests },
        });
    }

    private static JsonElement ExtractFirstResponse(JsonDocument document, string method)
    {
        var root = document.RootElement;

        if (root.TryGetProperty("error_code", out var topErr) && topErr.GetInt32() != 0)
        {
            throw new TapoApiException(topErr.GetInt32(), method, ErrorCodes.TryGetName(topErr.GetInt32()));
        }

        if (!root.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("responses", out var responses) ||
            responses.ValueKind != JsonValueKind.Array ||
            responses.GetArrayLength() == 0)
        {
            throw new TapoProtocolException("multipleRequest response is missing the responses array.");
        }

        var first = responses[0];
        if (first.TryGetProperty("error_code", out var err) && err.GetInt32() != 0)
        {
            var inner = err.GetInt32();
            throw new TapoApiException(inner, method, ErrorCodes.TryGetName(inner));
        }

        return first;
    }

    private static JsonDocument CloneToDocument(JsonElement element)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            element.WriteTo(writer);
        }

        ms.Position = 0;
        return JsonDocument.Parse(ms.ToArray());
    }
}
