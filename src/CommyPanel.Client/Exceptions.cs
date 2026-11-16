using System.Text;

namespace CommyPanel;

/// <summary>The base of every error this library raises.</summary>
public class CommyPanelException : Exception
{
    public CommyPanelException(string message) : base(message) { }

    public CommyPanelException(string message, Exception? inner) : base(message, inner) { }
}

/// <summary>The panel address is not https (or loopback http, when allowed).</summary>
public sealed class InsecureUrlException : CommyPanelException
{
    public InsecureUrlException()
        : base("commypanel: the panel address must be https. (Plain http is only allowed for localhost, 127.0.0.1 and ::1, and only with AllowHttpLoopback.)") { }
}

/// <summary>The panel address is not a plain scheme://host[:port].</summary>
public sealed class InvalidUrlException : CommyPanelException
{
    public InvalidUrlException()
        : base("commypanel: the panel address must look like https://panel.example.com") { }
}

/// <summary>The token is empty or has characters a token cannot have. The token is never part of the message.</summary>
public sealed class InvalidTokenException : CommyPanelException
{
    public InvalidTokenException()
        : base("commypanel: the token is empty or has characters a token cannot have") { }
}

/// <summary>An app name or variable name that cannot be valid. Raised before any request is made.</summary>
public sealed class InvalidInputException : CommyPanelException
{
    public InvalidInputException()
        : base("commypanel: invalid app or variable name") { }
}

/// <summary>An answer is bigger than the client accepts.</summary>
public sealed class ResponseTooLargeException : CommyPanelException
{
    public ResponseTooLargeException() : base("commypanel: the answer is too large") { }
}

/// <summary>
/// The panel answered with a redirect. Redirects are never followed, so a token cannot be handed to another address.
/// </summary>
public sealed class RedirectException : CommyPanelException
{
    public RedirectException()
        : base("commypanel: the panel answered with a redirect, which is not followed (check the address, https and www)") { }
}

/// <summary>An answer that is not the JSON the API gives (a proxy, a wrong address). The body is never echoed.</summary>
public sealed class InvalidResponseException : CommyPanelException
{
    public InvalidResponseException(string message) : base(message) { }

    public InvalidResponseException(string message, Exception? inner) : base(message, inner) { }
}

/// <summary>An error answer from the panel.</summary>
public sealed class ApiException : CommyPanelException
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NoFields =
        new Dictionary<string, IReadOnlyList<string>>();

    public ApiException(
        int status,
        string message,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? fields = null,
        Deployment? deployment = null,
        int retryAfterSeconds = 0)
        : base(Compose(status, message, fields))
    {
        Status = status;
        Detail = message;
        Fields = fields ?? NoFields;
        Deployment = deployment;
        RetryAfterSeconds = retryAfterSeconds;
    }

    /// <summary>The HTTP status code.</summary>
    public int Status { get; }

    /// <summary>The panel's own sentence (the <c>error</c> field of the answer).</summary>
    public string Detail { get; }

    /// <summary>The problems of a 422, by field name.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Fields { get; }

    /// <summary>The deployment that is already running, on a 409 that says so.</summary>
    public Deployment? Deployment { get; }

    /// <summary>How long the panel asked to wait, in seconds, when it did (429).</summary>
    public int RetryAfterSeconds { get; }

    /// <summary>404: no such app, deployment or variable, or an app the token may not use.</summary>
    public bool IsNotFound => Status == 404;

    /// <summary>401: no token, or an invalid, revoked or expired one.</summary>
    public bool IsUnauthorized => Status == 401;

    /// <summary>403: the token lacks the ability, or its owner lacks the permission.</summary>
    public bool IsForbidden => Status == 403;

    /// <summary>409: the state does not allow it, for example a deployment is already running.</summary>
    public bool IsConflict => Status == 409;

    /// <summary>429: too many calls.</summary>
    public bool IsRateLimited => Status == 429;

    private static string Compose(int status, string message, IReadOnlyDictionary<string, IReadOnlyList<string>>? fields)
    {
        var text = new StringBuilder("commypanel: ").Append(status).Append(' ').Append(message);

        if (fields is not null)
        {
            foreach (var name in fields.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                text.Append(" (").Append(name).Append(": ").Append(string.Join(' ', fields[name])).Append(')');
            }
        }

        return text.ToString();
    }
}
