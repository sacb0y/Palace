using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Windows.Security.Authentication.Web;

namespace Palace.Services.Cloud;

public static class CloudOAuth
{
    public static Uri CallbackUri => WebAuthenticationBroker.GetCurrentApplicationCallbackUri();

    public static async Task<CloudAuthTokens> AuthenticateAsync(
        Uri authorizeUri,
        Uri tokenUri,
        string clientId,
        string? extraTokenBody,
        CancellationToken ct = default)
    {
        var redirect = CallbackUri.ToString().TrimEnd('/');
        var verifier = CreateCodeVerifier();
        var challenge = CreateCodeChallenge(verifier);
        var state = CreateCodeVerifier()[..16];
        var start = new Uri(authorizeUri.ToString()
            + (authorizeUri.Query.Length == 0 ? "?" : "&")
            + "response_type=code"
            + "&client_id=" + Uri.EscapeDataString(clientId)
            + "&redirect_uri=" + Uri.EscapeDataString(redirect)
            + "&code_challenge=" + Uri.EscapeDataString(challenge)
            + "&code_challenge_method=S256"
            + "&state=" + Uri.EscapeDataString(state));

        var result = await WebAuthenticationBroker.AuthenticateAsync(
            WebAuthenticationOptions.None,
            start,
            new Uri(redirect));
        if (result.ResponseStatus == WebAuthenticationStatus.UserCancel)
        {
            throw new CloudAuthException("Sign-in was cancelled.");
        }

        if (result.ResponseStatus != WebAuthenticationStatus.Success)
        {
            throw new CloudAuthException("Sign-in failed.");
        }

        var response = new Uri(result.ResponseData);
        var query = ParseQuery(response);
        if (query.TryGetValue("error", out var error))
        {
            throw new CloudAuthException(error);
        }

        if (!query.TryGetValue("code", out var code) || string.IsNullOrEmpty(code))
        {
            throw new CloudAuthException("The sign-in response did not include an authorization code.");
        }

        if (query.TryGetValue("state", out var returnedState) &&
            !string.Equals(returnedState, state, StringComparison.Ordinal))
        {
            throw new CloudAuthException("The sign-in response failed the state check.");
        }

        var body = "grant_type=authorization_code"
            + "&code=" + Uri.EscapeDataString(code)
            + "&redirect_uri=" + Uri.EscapeDataString(redirect)
            + "&client_id=" + Uri.EscapeDataString(clientId)
            + "&code_verifier=" + Uri.EscapeDataString(verifier)
            + extraTokenBody;

        return await ExchangeTokenAsync(tokenUri, body, ct).ConfigureAwait(false);
    }

    public static async Task<CloudAuthTokens> RefreshAsync(
        Uri tokenUri,
        string clientId,
        string refreshToken,
        string? extraTokenBody,
        CancellationToken ct = default)
    {
        var body = "grant_type=refresh_token"
            + "&refresh_token=" + Uri.EscapeDataString(refreshToken)
            + "&client_id=" + Uri.EscapeDataString(clientId)
            + extraTokenBody;
        var tokens = await ExchangeTokenAsync(tokenUri, body, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(tokens.RefreshToken))
        {
            tokens.RefreshToken = refreshToken;
        }

        return tokens;
    }

    public static async Task<CloudAuthTokens> ExchangeTokenAsync(Uri tokenUri, string body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUri);
        request.Content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded");
        using var response = await SharedHttp.Client.SendAsync(request, ct).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new CloudAuthException(ReadError(json) ?? "Token exchange failed.");
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var access = root.TryGetProperty("access_token", out var at) ? at.GetString() : null;
        if (string.IsNullOrEmpty(access))
        {
            throw new CloudAuthException("The token response did not include an access token.");
        }

        var expires = root.TryGetProperty("expires_in", out var exp) && exp.TryGetInt32(out var seconds)
            ? DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, seconds - 120))
            : DateTimeOffset.UtcNow.AddMinutes(50);
        return new CloudAuthTokens
        {
            AccessToken = access,
            RefreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() ?? "" : "",
            ExpiresAt = expires
        };
    }

    public static async Task<JsonDocument> GetJsonAsync(string url, string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await SharedHttp.Client.SendAsync(request, ct).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new CloudAuthException(ReadError(json) ?? $"Request failed ({(int)response.StatusCode}).");
        }

        return JsonDocument.Parse(json);
    }

    public static async Task<JsonDocument> PostJsonAsync(string url, string accessToken, string jsonBody, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        using var response = await SharedHttp.Client.SendAsync(request, ct).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new CloudAuthException(ReadError(json) ?? $"Request failed ({(int)response.StatusCode}).");
        }

        return JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
    }

    public static async Task<Stream?> GetStreamAsync(string url, string? accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        var response = await SharedHttp.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            return null;
        }

        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return new HttpResponseStream(response, stream);
    }

    public static string CreateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64Url(bytes);
    }

    public static string CreateCodeChallenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64Url(hash);
    }

    public static Dictionary<string, string> ParseQuery(Uri uri)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var query = string.IsNullOrEmpty(uri.Query) ? uri.Fragment.TrimStart('#') : uri.Query.TrimStart('?');
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            map[Uri.UnescapeDataString(part[..eq])] = Uri.UnescapeDataString(part[(eq + 1)..].Replace('+', ' '));
        }

        return map;
    }

    public static string? ReadError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error_description", out var desc))
            {
                return desc.GetString();
            }

            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String)
                {
                    return error.GetString();
                }

                if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var message))
                {
                    return message.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // ignore
        }

        return null;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

internal static class SharedHttp
{
    public static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };
}

internal sealed class HttpResponseStream : Stream
{
    private readonly HttpResponseMessage _response;
    private readonly Stream _inner;

    public HttpResponseStream(HttpResponseMessage response, Stream inner)
    {
        _response = response;
        _inner = inner;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;
    public override long Position { get => _inner.Position; set => _inner.Position = value; }
    public override void Flush() => _inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
            _response.Dispose();
        }

        base.Dispose(disposing);
    }
}
