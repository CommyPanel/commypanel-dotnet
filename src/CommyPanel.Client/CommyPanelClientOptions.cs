namespace CommyPanel;

/// <summary>Options for <see cref="CommyPanelClient"/>.</summary>
public sealed class CommyPanelClientOptions
{
    /// <summary>
    /// Allows a plain http address, but only for localhost, 127.0.0.1 and ::1 (for development against a panel on
    /// the same machine). Any other http address is still refused.
    /// </summary>
    public bool AllowHttpLoopback { get; set; }

    /// <summary>
    /// A certificate authority, in PEM, to trust besides the system's. Use it for a panel whose certificate comes from
    /// a private CA. There is deliberately no option to skip verification.
    /// </summary>
    public string? CaCertificatePem { get; set; }

    /// <summary>The time one request may take, answer included (default 30 seconds).</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How many times a failed request may be tried again (default 2, at most 5). Only requests that are safe to
    /// repeat are retried: any request the panel refused with 429, and reading calls that met a 502, 503 or 504.
    /// </summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>The size of an answer the client will read, in bytes (default 16 MiB).</summary>
    public long MaxResponseBytes { get; set; } = 16L << 20;

    /// <summary>A name (your tool) put in front of the library's User-Agent.</summary>
    public string? UserAgentPrefix { get; set; }

    /// <summary>For tests: how to wait between retries and polls.</summary>
    internal Func<TimeSpan, CancellationToken, Task>? Delay { get; set; }

    internal void Validate()
    {
        if (RequestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(RequestTimeout), "the timeout must be positive");
        }

        if (MaxRetries is < 0 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxRetries), "retries must be between 0 and 5");
        }

        if (MaxResponseBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxResponseBytes), "the limit must be positive");
        }

        if (UserAgentPrefix is not null)
        {
            foreach (var c in UserAgentPrefix)
            {
                if (c < ' ' || c > '~')
                {
                    throw new ArgumentException("the user agent prefix must be printable ASCII", nameof(UserAgentPrefix));
                }
            }
        }
    }
}
