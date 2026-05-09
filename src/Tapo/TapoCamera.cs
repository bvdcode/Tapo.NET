using System;
using System.Threading;
using System.Threading.Tasks;
using Tapo.Control;
using Tapo.Devices;
using Tapo.Download;
using Tapo.Internal;
using Tapo.MediaStream;
using Tapo.Recordings;
using Tapo.Snapshots;

namespace Tapo;

/// <summary>
/// Aggregate root that wires up the control channel, the recordings client,
/// the device-command client and the media-stream factory so callers do not
/// have to plumb the layers themselves.
/// </summary>
public sealed class TapoCamera : ITapoCamera
{
    private readonly TapoCameraOptions _options;
    private readonly IControlChannel _control;
    private readonly bool _ownsControl;
    private readonly RecordingsClient _recordings;
    private readonly DeviceClient _device;
    private readonly TapoMethodInvoker _invoker;

    /// <summary>Creates a camera client and wires up the default control / device channels.</summary>
    /// <param name="options">Connection settings.</param>
    /// <param name="controlChannel">
    /// Optional pre-built control channel. Pass one to swap in a fake during
    /// tests; if <see langword="null"/>, a fresh
    /// <see cref="TapoControlChannel"/> is constructed and disposed with this
    /// instance.
    /// </param>
    public TapoCamera(TapoCameraOptions options, IControlChannel? controlChannel = null)
    {
        Throw.IfNull(options);
        Throw.IfNullOrEmpty(options.Host);
        Throw.IfNull(options.Password);
        Throw.IfNull(options.CloudPassword);

        _options = options;

        if (controlChannel is null)
        {
            _control = new TapoControlChannel(new ControlChannelOptions
            {
                Host = options.Host,
                Port = options.ControlPort,
                Username = options.Username,
                Password = options.Password,
                CloudPassword = options.CloudPassword,
                TrustAnyServerCertificate = options.TrustAnyServerCertificate,
            });
            _ownsControl = true;
        }
        else
        {
            _control = controlChannel;
            _ownsControl = false;
        }

        _recordings = new RecordingsClient(_control);
        _invoker = new TapoMethodInvoker(_control);
        _device = new DeviceClient(_invoker);
    }

    /// <summary>Hashing algorithm currently negotiated with the camera.</summary>
    public EncryptionMethod EncryptionMethod => _control.EncryptionMethod;

    /// <inheritdoc />
    public IRecordingsClient Recordings => _recordings;

    /// <inheritdoc />
    public IDeviceClient Device => _device;

    /// <summary>Underlying invoker for callers that need to issue commands the typed clients do not cover.</summary>
    public TapoMethodInvoker Methods => _invoker;

    /// <summary>Underlying control channel — exposed for diagnostics and advanced scenarios.</summary>
    public IControlChannel ControlChannel => _control;

    /// <inheritdoc />
    public Task ConnectAsync(CancellationToken cancellationToken = default)
        => _control.AuthenticateAsync(cancellationToken);

    /// <inheritdoc />
    public IVideoDownloader CreateDownloader() => CreateDownloader(VideoDownloaderOptions.Default);

    /// <inheritdoc />
    public IVideoDownloader CreateDownloader(VideoDownloaderOptions downloaderOptions)
    {
        Throw.IfNull(downloaderOptions);
        return new VideoDownloader(BuildBaseSessionOptions(), _recordings, downloaderOptions);
    }

    /// <inheritdoc />
    public ISnapshotClient CreateSnapshotClient()
    {
        var sessionOptions = new MediaSessionOptions
        {
            Host = _options.Host,
            Port = _options.StreamPort,
            Username = _options.Username,
            CloudPassword = _options.CloudPassword,
            SuperSecretKey = _options.SuperSecretKey,
            EncryptionMethod = _control.EncryptionMethod,
            // Snapshots only need a single frame — keep the window tight so
            // the camera cannot get ahead of us with a queue we are about to drop.
            WindowSize = 1,
        };

        return new SnapshotClient(sessionOptions);
    }

    /// <inheritdoc />
    public IMediaSession CreateMediaSession(MediaSessionOptions? overrides = null)
    {
        var options = overrides ?? BuildBaseSessionOptions();
        return new MediaSession(options);
    }

    private MediaSessionOptions BuildBaseSessionOptions() => new()
    {
        Host = _options.Host,
        Port = _options.StreamPort,
        Username = _options.Username,
        CloudPassword = _options.CloudPassword,
        SuperSecretKey = _options.SuperSecretKey,
        EncryptionMethod = _control.EncryptionMethod,
        WindowSize = _options.DefaultWindowSize,
    };

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_ownsControl) await _control.DisposeAsync().ConfigureAwait(false);
    }
}
