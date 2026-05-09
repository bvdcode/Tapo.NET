using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Tapo.Recordings;

/// <summary>
/// High-level access to the camera's SD recording archive.
/// </summary>
public interface IRecordingsClient
{
    /// <summary>Returns the cached camera user id, fetching it if necessary.</summary>
    Task<int> GetUserIdAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the offset (in seconds) between local time and camera time.</summary>
    Task<long> GetTimeCorrectionAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists recordings for a single calendar day.</summary>
    Task<IReadOnlyList<Recording>> GetRecordingsAsync(DateTime date, CancellationToken cancellationToken = default);

    /// <summary>Lists recordings for an arbitrary half-open UTC time range.</summary>
    Task<IReadOnlyList<Recording>> GetRecordingsAsync(long startUnixTime, long endUnixTime, CancellationToken cancellationToken = default);
}
