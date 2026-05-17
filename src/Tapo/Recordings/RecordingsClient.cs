using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Tapo.Control;
using Tapo.Internal;

namespace Tapo.Recordings;

/// <summary>Default implementation backed by an <see cref="IControlChannel"/>.</summary>
public sealed class RecordingsClient : IRecordingsClient
{
    private readonly IControlChannel _control;
    private int? _cachedUserId;
    private long? _cachedTimeCorrection;

    /// <summary>Creates a new recordings client backed by the supplied <see cref="IControlChannel"/>.</summary>
    public RecordingsClient(IControlChannel control)
    {
        Throw.IfNull(control);
        _control = control;
    }

    /// <summary>Resolves the camera-side user id used to scope playback queries. Cached after the first call.</summary>
    public async Task<int> GetUserIdAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedUserId is { } cached) return cached;

        var request = JsonSerializer.SerializeToDocument(new
        {
            method = "multipleRequest",
            @params = new
            {
                requests = new object[]
                {
                    new { method = "getUserID", @params = new { system = new { get_user_id = "null" } } },
                },
            },
        }).RootElement;

        using var doc = await _control.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var first = NavigateToSingleResponse(doc);

        if (first.TryGetProperty("result", out var result) &&
            result.TryGetProperty("user_id", out var userIdElem) &&
            userIdElem.TryGetInt32(out var userId))
        {
            _cachedUserId = userId;
            return userId;
        }

        throw new TapoProtocolException("getUserID response did not contain a user_id.");
    }

    /// <summary>Returns the offset in seconds between the host clock and the camera clock (host − camera). Cached after the first call.</summary>
    public async Task<long> GetTimeCorrectionAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedTimeCorrection is { } cached) return cached;

        var request = JsonSerializer.SerializeToDocument(new
        {
            method = "multipleRequest",
            @params = new
            {
                requests = new object[]
                {
                    new { method = "getClockStatus", @params = new { system = new { name = "clock_status" } } },
                },
            },
        }).RootElement;

        using var doc = await _control.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var first = NavigateToSingleResponse(doc);

        if (!first.TryGetProperty("result", out var result)) throw new TapoProtocolException("Missing result in getClockStatus response.");
        if (!result.TryGetProperty("system", out var system)) throw new TapoProtocolException("Missing system in getClockStatus response.");
        if (!system.TryGetProperty("clock_status", out var clock)) throw new TapoProtocolException("Missing clock_status in getClockStatus response.");
        if (!clock.TryGetProperty("seconds_from_1970", out var sec) || !sec.TryGetInt64(out var cameraTime))
        {
            throw new TapoProtocolException("Missing seconds_from_1970 in getClockStatus response.");
        }

        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var correction = nowUnix - cameraTime;
        _cachedTimeCorrection = correction;
        return correction;
    }

    /// <summary>Lists all recordings stored on the camera for the supplied calendar day (camera-local time).</summary>
    public Task<IReadOnlyList<Recording>> GetRecordingsAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        var dateString = date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return GetRecordingsForDateAsync(dateString, cancellationToken);
    }

    /// <summary>Lists all recordings whose timestamp falls between <paramref name="startUnixTime"/> and <paramref name="endUnixTime"/> (Unix seconds).</summary>
    public async Task<IReadOnlyList<Recording>> GetRecordingsAsync(long startUnixTime, long endUnixTime, CancellationToken cancellationToken = default)
    {
        Throw.IfNegative(startUnixTime);
        Throw.IfNegative(endUnixTime);

        var userId = await GetUserIdAsync(cancellationToken).ConfigureAwait(false);

        var request = JsonSerializer.SerializeToDocument(new
        {
            method = "multipleRequest",
            @params = new
            {
                requests = new object[]
                {
                    new
                    {
                        method = "searchVideoWithUTC",
                        @params = new
                        {
                            playback = new
                            {
                                search_video_with_utc = new
                                {
                                    channel = 0,
                                    end_time = endUnixTime,
                                    end_index = 999_999_999,
                                    id = userId,
                                    start_index = 0,
                                    start_time = startUnixTime,
                                },
                            },
                        },
                    },
                },
            },
        }).RootElement;

        using var doc = await _control.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return ExtractRecordings(NavigateToSingleResponse(doc));
    }

    private async Task<IReadOnlyList<Recording>> GetRecordingsForDateAsync(string dateString, CancellationToken cancellationToken)
    {
        var userId = await GetUserIdAsync(cancellationToken).ConfigureAwait(false);

        var request = JsonSerializer.SerializeToDocument(new
        {
            method = "multipleRequest",
            @params = new
            {
                requests = new object[]
                {
                    new
                    {
                        method = "searchVideoOfDay",
                        @params = new
                        {
                            playback = new
                            {
                                search_video_utility = new
                                {
                                    channel = 0,
                                    date = dateString,
                                    end_index = 999_999_999,
                                    id = userId,
                                    start_index = 0,
                                },
                            },
                        },
                    },
                },
            },
        }).RootElement;

        using var doc = await _control.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return ExtractRecordings(NavigateToSingleResponse(doc));
    }

    private static JsonElement NavigateToSingleResponse(JsonDocument doc)
    {
        var root = doc.RootElement;
        if (root.TryGetProperty("error_code", out var topErr) && topErr.GetInt32() != 0)
        {
            throw new TapoApiException(topErr.GetInt32(), "multipleRequest", null);
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
            throw new TapoApiException(err.GetInt32(), first.TryGetProperty("method", out var m) ? m.GetString()! : "unknown", null);
        }

        return first;
    }

    private static IReadOnlyList<Recording> ExtractRecordings(JsonElement firstResponse)
    {
        if (!firstResponse.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("playback", out var playback))
        {
            return Array.Empty<Recording>();
        }

        // The camera answers with either `search_video_results` or
        // `search_video_results` keyed differently by entry — we handle both.
        if (!playback.TryGetProperty("search_video_results", out var resultsElem) ||
            resultsElem.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<Recording>();
        }

        var output = new List<Recording>(resultsElem.GetArrayLength());
        foreach (var entry in resultsElem.EnumerateArray())
        {
            // Each entry has either nested numbered keys ("0", "1", ...) or
            // flat fields depending on which firmware method answered
            // (searchVideoWithUTC vs searchVideoOfDay). We accept both.
            if (entry.ValueKind != JsonValueKind.Object) continue;

            foreach (var prop in entry.EnumerateObject())
            {
                var inner = prop.Value;
                if (inner.ValueKind != JsonValueKind.Object) continue;
                if (!inner.TryGetProperty("startTime", out var startElem) ||
                    !inner.TryGetProperty("endTime", out var endElem))
                {
                    continue;
                }

                var startTime = startElem.GetInt64();
                var endTime = endElem.GetInt64();
                int id = 0;
                if (inner.TryGetProperty("id", out var idElem) && idElem.TryGetInt32(out var idValue))
                {
                    id = idValue;
                }

                output.Add(new Recording(id, startTime, endTime));
            }
        }

        return output;
    }
}
