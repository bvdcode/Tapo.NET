using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tapo.Tests.Integration;

/// <summary>
/// Local test secrets loaded from <c>secrets.json</c>. The file lives next to
/// the test project, is git-ignored, and is copied to the test output
/// directory by the project file so xUnit can find it at runtime.
/// </summary>
internal sealed class TestSecrets
{
    [JsonPropertyName("host")]
    public string Host { get; set; } = string.Empty;

    [JsonPropertyName("username")]
    public string Username { get; set; } = "admin";

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;

    [JsonPropertyName("cloudPassword")]
    public string CloudPassword { get; set; } = string.Empty;

    [JsonPropertyName("controlPort")]
    public int ControlPort { get; set; } = 443;

    [JsonPropertyName("streamPort")]
    public int StreamPort { get; set; } = 8800;

    [JsonPropertyName("windowSize")]
    public int? WindowSize { get; set; }

    [JsonPropertyName("filePath")]
    [JsonIgnore]
    public string FilePath { get; set; } = string.Empty;

    public static TestSecrets? TryLoad()
    {
        // Preferred: the copy that the test project pins to its output
        // directory. Fall back to the source-tree files (when running from
        // a checkout) and then to the sibling console-test project (so a
        // single secrets file can feed both the sample and the tests).
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "secrets.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "secrets.json"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "secrets.json")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Tapo.ConsoleTest", "secrets.json")),
        };

        var path = candidates.FirstOrDefault(File.Exists);
        if (path is null) return null;

        var json = File.ReadAllText(path);
        var secrets = JsonSerializer.Deserialize<TestSecrets>(json);
        if (secrets is null) return null;

        secrets.FilePath = path;

        // Cloud password defaults to the local one; that mirrors the most
        // common camera setup.
        if (string.IsNullOrWhiteSpace(secrets.CloudPassword))
        {
            secrets.CloudPassword = secrets.Password;
        }

        return secrets;
    }
}
