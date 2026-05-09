using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Tapo.Authentication;
using Tapo.Cryptography;
using Tapo.Internal;

namespace Tapo.Control;

/// <summary>
/// Default <see cref="IControlChannel"/>. Talks to <c>https://host:443/</c>
/// using either the legacy <c>hashed=true</c> login flow or the modern
/// <c>encrypt_type=3</c> handshake with <c>securePassthrough</c> envelope.
/// The two flows are auto-detected on the first request.
/// </summary>
public sealed class TapoControlChannel : IControlChannel
{
    private readonly ControlChannelOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private string? _stok;
    private string? _cnonce;
    private string? _hashedMd5Pwd;
    private string? _hashedSha256Pwd;
    private byte[]? _lsk;
    private byte[]? _ivb;
    private int? _seq;
    private bool? _isSecure;

    public TapoControlChannel(ControlChannelOptions options, HttpClient? httpClient = null)
    {
        Throw.IfNull(options);
        Throw.IfNullOrEmpty(options.Host);
        Throw.IfNull(options.Password);
        Throw.IfNull(options.CloudPassword);

        _options = options;
        if (httpClient is null)
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
            };

            if (options.TrustAnyServerCertificate)
            {
                handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
            }

            _http = new HttpClient(handler, disposeHandler: true)
            {
                Timeout = TimeSpan.FromMilliseconds(options.TimeoutMilliseconds),
            };
            _ownsHttpClient = true;
        }
        else
        {
            _http = httpClient;
            _ownsHttpClient = false;
        }

        ConfigureDefaultHeaders();
    }

    public bool IsAuthenticated => !string.IsNullOrEmpty(_stok);

    public EncryptionMethod EncryptionMethod { get; private set; } = EncryptionMethod.Md5;

    public async Task AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        if (IsAuthenticated) return;

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsAuthenticated) return;
            await RefreshStokAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task<JsonDocument> SendAsync(JsonElement request, CancellationToken cancellationToken = default)
    {
        await AuthenticateAsync(cancellationToken).ConfigureAwait(false);
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await SendCoreAsync(request, retryOnAuthFailure: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task<JsonDocument> SendCoreAsync(JsonElement request, bool retryOnAuthFailure, CancellationToken ct)
    {
        var url = $"https://{_options.Host}:{_options.Port}/stok={_stok}/ds";
        var bodyJson = SerializeJson(request);

        HttpRequestMessage httpRequest;
        if (_isSecure == true && _seq is not null && _lsk is not null && _ivb is not null)
        {
            var encrypted = EncryptPayload(bodyJson);
            var wrapped = JsonSerializer.SerializeToDocument(new
            {
                method = "securePassthrough",
                @params = new { request = Convert.ToBase64String(encrypted) },
            }).RootElement;
            var wrappedJson = SerializeJson(wrapped);

            httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(wrappedJson, Encoding.UTF8, "application/json"),
            };
            httpRequest.Headers.TryAddWithoutValidation("Seq", _seq.Value.ToString(CultureInfo.InvariantCulture));
            httpRequest.Headers.TryAddWithoutValidation(
                "Tapo_tag",
                SecurePassthrough.ComputeRequestTag(
                    GetActiveHashedPassword(),
                    _cnonce!,
                    wrappedJson,
                    _seq.Value));

            _seq++;
        }
        else
        {
            httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(bodyJson, Encoding.UTF8, "application/json"),
            };
        }

        using var response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        var raw = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        var doc = JsonDocument.Parse(raw);
        if (_isSecure == true && doc.RootElement.TryGetProperty("result", out var resultElem) &&
            resultElem.TryGetProperty("response", out var encryptedB64))
        {
            var cipherBytes = Convert.FromBase64String(encryptedB64.GetString() ?? string.Empty);
            byte[] decrypted;
            try
            {
                decrypted = DecryptPayload(cipherBytes);
            }
            catch (CryptographicException) when (retryOnAuthFailure)
            {
                doc.Dispose();
                ClearSession();
                await RefreshStokAsync(ct).ConfigureAwait(false);
                return await SendCoreAsync(request, retryOnAuthFailure: false, ct).ConfigureAwait(false);
            }

            doc.Dispose();
            doc = JsonDocument.Parse(decrypted);
        }

        return doc;
    }

    private async Task RefreshStokAsync(CancellationToken ct)
    {
        ClearSession();

        _hashedMd5Pwd = PasswordHasher.HashPasswordHex(_options.Password, EncryptionMethod.Md5);
        _hashedSha256Pwd = PasswordHasher.HashPasswordHex(_options.Password, EncryptionMethod.Sha256);
        _cnonce = NonceGenerator.Generate(8).ToUpperInvariant();
        _isSecure = await DetectSecureConnectionAsync(ct).ConfigureAwait(false);

        var loginUrl = $"https://{_options.Host}:{_options.Port}";

        if (_isSecure == true)
        {
            await SecureLoginAsync(loginUrl, ct).ConfigureAwait(false);
        }
        else
        {
            await InsecureLoginAsync(loginUrl, ct).ConfigureAwait(false);
            EncryptionMethod = EncryptionMethod.Md5;
        }
    }

    private async Task<bool> DetectSecureConnectionAsync(CancellationToken ct)
    {
        var url = $"https://{_options.Host}:{_options.Port}";
        var probe = new
        {
            method = "login",
            @params = new
            {
                encrypt_type = "3",
                username = _options.Username,
                cnonce = _cnonce,
            },
        };

        using var doc = await PostJsonAsync(url, probe, ct).ConfigureAwait(false);
        var root = doc.RootElement;
        if (!root.TryGetProperty("error_code", out var errorElement)) return false;
        if (errorElement.GetInt32() != -40413) return false;
        if (!root.TryGetProperty("result", out var result)) return false;
        if (!result.TryGetProperty("data", out var data)) return false;
        if (!data.TryGetProperty("encrypt_type", out var encryptTypes)) return false;

        if (encryptTypes.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in encryptTypes.EnumerateArray())
            {
                if (item.GetString() == "3") return true;
            }
        }
        else if (encryptTypes.ValueKind == JsonValueKind.String)
        {
            return encryptTypes.GetString()!.Contains('3');
        }

        return false;
    }

    private async Task InsecureLoginAsync(string url, CancellationToken ct)
    {
        var loginPayload = new
        {
            method = "login",
            @params = new
            {
                hashed = true,
                password = _hashedMd5Pwd,
                username = _options.Username,
            },
        };

        using var doc = await PostJsonAsync(url, loginPayload, ct).ConfigureAwait(false);
        EnsureNoError(doc.RootElement, "login");

        if (doc.RootElement.TryGetProperty("result", out var result) &&
            result.TryGetProperty("stok", out var stok))
        {
            _stok = stok.GetString();
            return;
        }

        throw new TapoAuthenticationException("Login response did not contain a stok value.");
    }

    private async Task SecureLoginAsync(string url, CancellationToken ct)
    {
        var firstStage = new
        {
            method = "login",
            @params = new
            {
                cnonce = _cnonce,
                encrypt_type = "3",
                username = _options.Username,
            },
        };

        using (var doc = await PostJsonAsync(url, firstStage, ct).ConfigureAwait(false))
        {
            var root = doc.RootElement;
            if (!root.TryGetProperty("result", out var result) ||
                !result.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("nonce", out var nonceElem) ||
                !data.TryGetProperty("device_confirm", out var deviceConfirmElem))
            {
                throw new TapoAuthenticationException("Camera did not return a nonce/device_confirm pair.");
            }

            var nonce = nonceElem.GetString()!;
            var deviceConfirm = deviceConfirmElem.GetString()!;
            if (!ValidateDeviceConfirm(nonce, deviceConfirm))
            {
                throw new TapoAuthenticationException("device_confirm validation failed — credentials likely wrong.");
            }

            var hashedPwd = GetActiveHashedPassword();
            var digestPasswd = SecurePassthrough.Sha256Upper(hashedPwd + _cnonce + nonce) + _cnonce + nonce;

            var secondStage = new
            {
                method = "login",
                @params = new
                {
                    cnonce = _cnonce,
                    encrypt_type = "3",
                    digest_passwd = digestPasswd,
                    username = _options.Username,
                },
            };

            using var loginDoc = await PostJsonAsync(url, secondStage, ct).ConfigureAwait(false);
            var loginRoot = loginDoc.RootElement;
            EnsureNoError(loginRoot, "login");

            if (!loginRoot.TryGetProperty("result", out var loginResult) ||
                !loginResult.TryGetProperty("stok", out var stokElem) ||
                !loginResult.TryGetProperty("start_seq", out var seqElem))
            {
                throw new TapoAuthenticationException("Camera did not return stok/start_seq after digest login.");
            }

            _stok = stokElem.GetString();
            _seq = seqElem.GetInt32();
            _lsk = SecurePassthrough.DeriveToken("lsk", _cnonce!, hashedPwd, nonce);
            _ivb = SecurePassthrough.DeriveToken("ivb", _cnonce!, hashedPwd, nonce);
        }
    }

    private bool ValidateDeviceConfirm(string nonce, string deviceConfirm)
    {
        var sha256Hash = SecurePassthrough.Sha256Upper(_cnonce + _hashedSha256Pwd + nonce);
        var md5Hash = SecurePassthrough.Sha256Upper(_cnonce + _hashedMd5Pwd + nonce);

        if (deviceConfirm == sha256Hash + nonce + _cnonce)
        {
            EncryptionMethod = EncryptionMethod.Sha256;
            return true;
        }

        if (deviceConfirm == md5Hash + nonce + _cnonce)
        {
            EncryptionMethod = EncryptionMethod.Md5;
            return true;
        }

        return false;
    }

    private string GetActiveHashedPassword() => EncryptionMethod switch
    {
        EncryptionMethod.Sha256 => _hashedSha256Pwd!,
        _ => _hashedMd5Pwd!,
    };

    private byte[] EncryptPayload(string json)
    {
        using var cipher = new AesCbcCipher(_lsk!, _ivb!);
        return cipher.Encrypt(Encoding.UTF8.GetBytes(json));
    }

    private byte[] DecryptPayload(byte[] data)
    {
        using var cipher = new AesCbcCipher(_lsk!, _ivb!);
        return cipher.Decrypt(data);
    }

    private async Task<JsonDocument> PostJsonAsync<TBody>(string url, TBody body, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(body);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        var raw = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        try
        {
            return JsonDocument.Parse(raw);
        }
        catch (JsonException ex)
        {
            throw new TapoProtocolException($"Camera returned non-JSON content: '{Truncate(raw, 256)}'.")
            {
                Source = ex.Source,
            };
        }
    }

    private static string SerializeJson(JsonElement element)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            element.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void EnsureNoError(JsonElement element, string method)
    {
        if (!element.TryGetProperty("error_code", out var code)) return;
        var value = code.GetInt32();
        if (value == 0) return;

        var name = ErrorCodes.TryGetName(value);
        throw new TapoApiException(value, method, name);
    }

    private static string Truncate(string input, int max) => input.Length <= max ? input : input.Substring(0, max);

    private void ConfigureDefaultHeaders()
    {
        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.AcceptEncoding.Clear();
        _http.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
        _http.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));
        _http.DefaultRequestHeaders.UserAgent.Clear();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Tapo CameraClient Android");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("requestByApp", "true");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", $"https://{_options.Host}:{_options.Port}");
    }

    private void ClearSession()
    {
        _stok = null;
        _seq = null;
        _lsk = null;
        _ivb = null;
    }

    public ValueTask DisposeAsync()
    {
        _sendLock.Dispose();
        if (_ownsHttpClient) _http.Dispose();
        return default;
    }
}
