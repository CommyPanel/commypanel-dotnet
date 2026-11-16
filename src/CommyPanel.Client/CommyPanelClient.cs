using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace CommyPanel;

/// <summary>
/// A client for one CommyPanel server with one token. It is safe for concurrent use. Dispose it when you are done.
/// </summary>
/// <remarks>
/// Built to be hard to misuse: the address must be https (loopback http only when asked), redirects are never followed,
/// TLS is always verified, the token is only ever in the Authorization header (never in errors, ToString or the
/// debugger), calls have a timeout, bounded retries and a size limit on answers, and names are checked before any
/// request. Environment variable values can only be written, because the API never returns them.
/// </remarks>
[DebuggerDisplay("CommyPanelClient {BaseUrl}")]
public sealed partial class CommyPanelClient : IDisposable
{
    /// <summary>The version of this library, sent in the User-Agent.</summary>
    public const string Version = "0.1.0";

    private const string BasePath = "/api/v1";
    private const int MaxErrorBodyBytes = 1 << 20;
    private static readonly TimeSpan MaxRetryWait = TimeSpan.FromSeconds(30);

    internal static readonly JsonSerializerOptions Json = new();

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly string _token;

    private readonly HttpClient _http;
    private readonly X509Certificate2Collection? _roots;
    private readonly string _agent;
    private readonly int _maxRetries;
    private readonly long _maxBody;
    private readonly TimeSpan _timeout;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <summary>Makes a client for the panel at <paramref name="baseUrl"/> (https://panel.example.com) with a token.</summary>
    /// <exception cref="InsecureUrlException">The address is not https.</exception>
    /// <exception cref="InvalidUrlException">The address is not a plain scheme://host[:port].</exception>
    /// <exception cref="InvalidTokenException">The token is empty or has characters a token cannot have.</exception>
    public CommyPanelClient(string baseUrl, string token, CommyPanelClientOptions? options = null)
    {
        options ??= new CommyPanelClientOptions();
        options.Validate();

        BaseUrl = CheckBaseUrl(baseUrl, options.AllowHttpLoopback);
        Validation.Token(token);

        _token = token;
        _timeout = options.RequestTimeout;
        _maxRetries = options.MaxRetries;
        _maxBody = options.MaxResponseBytes;
        _delay = options.Delay ?? ((span, cancel) => Task.Delay(span, cancel));
        _agent = "commypanel-dotnet/" + Version;

        if (!string.IsNullOrWhiteSpace(options.UserAgentPrefix))
        {
            _agent = options.UserAgentPrefix.Trim() + " " + _agent;
        }

        var handler = new SocketsHttpHandler
        {
            // The token must never go to another address: a redirect is an error, not something to follow.
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None,
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            },
        };

        if (!string.IsNullOrEmpty(options.CaCertificatePem))
        {
            _roots = TlsTrust.ParseRoots(options.CaCertificatePem);
            handler.SslOptions.RemoteCertificateValidationCallback = TlsTrust.Callback(_roots);
        }

        // The timeout is applied per request below, so it covers reading the answer too.
        _http = new HttpClient(handler, disposeHandler: true) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    }

    /// <summary>The panel address the client uses (scheme://host[:port]).</summary>
    public string BaseUrl { get; }

    /// <summary>Describes the client without the token.</summary>
    public override string ToString() => "CommyPanelClient{" + BaseUrl + ", token: redacted}";

    public void Dispose()
    {
        _http.Dispose();

        if (_roots is not null)
        {
            foreach (var certificate in _roots)
            {
                certificate.Dispose();
            }
        }
    }

    private static string CheckBaseUrl(string? raw, bool allowHttpLoopback)
    {
        if (string.IsNullOrWhiteSpace(raw) || !Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri) || uri.DnsSafeHost.Length == 0)
        {
            throw new InvalidUrlException();
        }

        var path = uri.AbsolutePath.TrimEnd('/');

        if (uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || (path.Length > 0 && path != BasePath))
        {
            throw new InvalidUrlException();
        }

        switch (uri.Scheme)
        {
            case "https":
                break;
            case "http":
                if (!allowHttpLoopback || !IsLoopback(uri.DnsSafeHost))
                {
                    throw new InsecureUrlException();
                }

                break;
            default:
                throw new InsecureUrlException();
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }

    private static bool IsLoopback(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip));

    /// <summary>Makes one call, with retries, and decodes the "data" of the answer.</summary>
    private async Task<(int Status, T Data)> SendAsync<T>(
        HttpMethod method,
        string path,
        IReadOnlyList<(string Name, string Value)>? query,
        object? body,
        CancellationToken cancellationToken)
    {
        byte[]? payload = body is null ? null : JsonSerializer.SerializeToUtf8Bytes(body, Json);

        var target = new StringBuilder(BaseUrl).Append(BasePath).Append(path);

        if (query is { Count: > 0 })
        {
            target.Append('?').Append(string.Join('&', query.Select(q => Uri.EscapeDataString(q.Name) + "=" + Uri.EscapeDataString(q.Value))));
        }

        var url = target.ToString();

        for (var attempt = 0; ; attempt++)
        {
            var outcome = await OnceAsync(method, url, path, payload, cancellationToken).ConfigureAwait(false);

            if (outcome.Error is null)
            {
                return (outcome.Status, Decode<T>(outcome.Body));
            }

            if (attempt >= _maxRetries || !Retryable(method, outcome.Status))
            {
                throw outcome.Error;
            }

            var wait = outcome.RetryAfter > TimeSpan.Zero
                ? outcome.RetryAfter
                : TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt));

            await _delay(wait > MaxRetryWait ? MaxRetryWait : wait, cancellationToken).ConfigureAwait(false);
        }
    }

    // A 429 means the panel did not do the work, so any call may be repeated; a gateway error is only
    // repeated for calls that read, because a write may have happened.
    private static bool Retryable(HttpMethod method, int status) => status switch
    {
        429 => true,
        502 or 503 or 504 => method == HttpMethod.Get,
        _ => false,
    };

    private sealed record Outcome(int Status, byte[] Body, TimeSpan RetryAfter, Exception? Error);

    private async Task<Outcome> OnceAsync(HttpMethod method, string url, string path, byte[]? payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("User-Agent", _agent);

        if (payload is not null)
        {
            request.Content = new ByteArrayContent(payload);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);

        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            var status = (int)response.StatusCode;

            if (status is >= 300 and < 400)
            {
                return new Outcome(status, Array.Empty<byte>(), TimeSpan.Zero, new RedirectException());
            }

            var limit = status >= 400 ? Math.Min(_maxBody, MaxErrorBodyBytes) : _maxBody;
            var body = await ReadLimitedAsync(response, limit, timeout.Token).ConfigureAwait(false);

            if (status >= 400)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.Zero;

                return new Outcome(status, body, retryAfter, ParseError(status, body, retryAfter));
            }

            return new Outcome(status, body, TimeSpan.Zero, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CommyPanelException($"commypanel: {method} {path}: the request timed out after {_timeout.TotalSeconds:0.#} seconds");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or AuthenticationException)
        {
            // The address has no secret in it, but keep only the cause and make sure the token cannot be repeated.
            throw new CommyPanelException($"commypanel: {method} {path}: {Scrub(ex.Message)}", ex);
        }
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpResponseMessage response, long limit, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is { } declared && declared > limit)
        {
            throw new ResponseTooLargeException();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        long total = 0;

        while (true)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false);

            if (read == 0)
            {
                return buffer.ToArray();
            }

            total += read;

            if (total > limit)
            {
                throw new ResponseTooLargeException();
            }

            buffer.Write(chunk, 0, read);
        }
    }

    private T Decode<T>(byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("data", out var data))
            {
                throw new InvalidResponseException(NotTheApi());
            }

            var value = data.Deserialize<T>(Json);

            return value ?? throw new InvalidResponseException(NotTheApi());
        }
        catch (JsonException ex)
        {
            throw new InvalidResponseException(NotTheApi(), ex);
        }
    }

    private string NotTheApi() => $"commypanel: the answer is not the JSON the API gives (is {BaseUrl} a CommyPanel?)";

    // Takes the token out of what the panel said. A misbehaving (or impersonated) server could echo the Authorization
    // header into its error text, and that text goes to the caller's logs.
    private ApiException ParseError(int status, byte[] body, TimeSpan retryAfter)
    {
        string? message = null;
        var fields = new Dictionary<string, IReadOnlyList<string>>();
        Deployment? deployment = null;

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                message = StringOf(root, "error");

                if (string.IsNullOrEmpty(message))
                {
                    message = StringOf(root, "message");
                }

                if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in errors.EnumerateObject())
                    {
                        if (property.Value.ValueKind == JsonValueKind.Array)
                        {
                            fields[property.Name] = property.Value.EnumerateArray()
                                .Where(e => e.ValueKind == JsonValueKind.String)
                                .Select(e => e.GetString() ?? "")
                                .ToList();
                        }
                    }
                }

                if (root.TryGetProperty("deployment", out var running) && running.ValueKind == JsonValueKind.Object)
                {
                    try
                    {
                        deployment = running.Deserialize<Deployment>(Json);
                    }
                    catch (JsonException)
                    {
                        // A deployment that cannot be read is not worth losing the error over.
                    }
                }
            }
        }
        catch (JsonException)
        {
            message = null;
        }

        // A proxy or a crash answered with something that is not the API's JSON: say what it was, not what it said.
        if (string.IsNullOrEmpty(message))
        {
            return new ApiException(status, $"HTTP {status} (not an answer from the CommyPanel API)", null, null, (int)retryAfter.TotalSeconds);
        }

        var clean = new Dictionary<string, IReadOnlyList<string>>();

        foreach (var (name, sentences) in fields)
        {
            clean[Scrub(name)] = sentences.Select(sentence => Scrub(sentence)).ToList();
        }

        if (deployment is not null)
        {
            deployment = deployment with
            {
                Status = Scrub(deployment.Status),
                Stage = deployment.Stage is null ? null : Scrub(deployment.Stage),
                Commit = deployment.Commit is null ? null : Scrub(deployment.Commit),
                CommitMessage = deployment.CommitMessage is null ? null : Scrub(deployment.CommitMessage),
                Error = deployment.Error is null ? null : Scrub(deployment.Error),
                Url = Scrub(deployment.Url),
                Log = deployment.Log is null ? null : Scrub(deployment.Log),
            };
        }

        return new ApiException(status, Scrub(message), clean, deployment, (int)retryAfter.TotalSeconds);
    }

    private static string? StringOf(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private string Scrub(string text) => text.Replace(_token, "[redacted]", StringComparison.Ordinal);
}
