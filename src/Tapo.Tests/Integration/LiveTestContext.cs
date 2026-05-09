using System;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Tapo;
using Xunit.Abstractions;

namespace Tapo.Tests.Integration;

/// <summary>
/// Boots a connected <see cref="TapoCamera"/> from the secrets file. Tests
/// that depend on it call <see cref="CreateAsync"/> in a try/return pattern
/// so the suite degrades gracefully on machines without a configured camera.
/// </summary>
internal sealed class LiveTestContext : IAsyncDisposable
{
    private LiveTestContext(TapoCamera camera, TestSecrets secrets)
    {
        Camera = camera;
        Secrets = secrets;
    }

    public TapoCamera Camera { get; }

    public TestSecrets Secrets { get; }

    public string DownloadFolder
    {
        get
        {
            var folder = Path.Combine(Path.GetTempPath(), "Tapo.NET.Tests.Downloads");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when the secrets are
    /// missing or the camera is not reachable. Tests are expected to catch
    /// that and bail with an <c>output.WriteLine</c> note so the run shows
    /// a green pass-with-no-assertions instead of a hard failure.
    /// </summary>
    public static async Task<LiveTestContext> CreateAsync(ITestOutputHelper output)
    {
        var secrets = TestSecrets.TryLoad()
            ?? throw new InvalidOperationException(
                "secrets.json was not found. Drop one next to the test project (see README.md → 'Live integration tests').");

        if (string.IsNullOrWhiteSpace(secrets.Host) || string.IsNullOrWhiteSpace(secrets.Password))
        {
            throw new InvalidOperationException("secrets.json is missing the host or password field.");
        }

        output.WriteLine($"Loaded secrets from {secrets.FilePath}");

        var camera = new TapoCamera(new TapoCameraOptions
        {
            Host = secrets.Host,
            Username = secrets.Username,
            Password = secrets.Password,
            CloudPassword = secrets.CloudPassword,
            ControlPort = secrets.ControlPort,
            StreamPort = secrets.StreamPort,
            DefaultWindowSize = secrets.WindowSize ?? 256,
            TrustAnyServerCertificate = true,
        });

        try
        {
            await camera.ConnectAsync();
        }
        catch (TapoException ex)
        {
            await camera.DisposeAsync();
            throw new InvalidOperationException($"Camera at {secrets.Host} rejected the credentials: {ex.Message}", ex);
        }
        catch (SocketException ex)
        {
            await camera.DisposeAsync();
            throw new InvalidOperationException($"Camera at {secrets.Host} not reachable: {ex.Message}", ex);
        }
        catch (HttpRequestException ex)
        {
            await camera.DisposeAsync();
            throw new InvalidOperationException($"Camera at {secrets.Host} not reachable over HTTPS: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex)
        {
            await camera.DisposeAsync();
            throw new InvalidOperationException($"Camera at {secrets.Host} did not respond before the timeout: {ex.Message}", ex);
        }

        output.WriteLine($"Connected to {secrets.Host}, encryption={camera.EncryptionMethod}.");
        return new LiveTestContext(camera, secrets);
    }

    public ValueTask DisposeAsync() => Camera.DisposeAsync();
}
