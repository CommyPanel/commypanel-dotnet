using System.Security.Cryptography.X509Certificates;

namespace CommyPanel.Tests;

internal static class TestKit
{
    /// <summary>A token that must never show up anywhere.</summary>
    public const string Token = "7|cpt_secretsecretsecretsecretsecretsecret";

    public static Func<Recorded, int, Reply> Always(int status, string body) => (_, _) => new Reply(status, body);

    /// <summary>A client for a plain-http fake panel that does not really sleep between retries.</summary>
    public static CommyPanelClient Client(FakePanel panel, Action<CommyPanelClientOptions>? configure = null, List<TimeSpan>? waits = null)
    {
        var options = new CommyPanelClientOptions
        {
            AllowHttpLoopback = true,
            Delay = (span, cancel) =>
            {
                waits?.Add(span);
                cancel.ThrowIfCancellationRequested();

                return Task.CompletedTask;
            },
        };

        configure?.Invoke(options);

        return new CommyPanelClient(panel.Url, Token, options);
    }

    public static string Fixture(string file) => Path.Combine(AppContext.BaseDirectory, "fixtures", "tls", file);

    /// <summary>A test certificate with its key, in a form Kestrel can use on every platform.</summary>
    public static X509Certificate2 LoadCertificate(string name)
    {
        using var withKey = X509Certificate2.CreateFromPemFile(Fixture(name + ".pem"), Fixture(name + ".key"));

        return new X509Certificate2(withKey.Export(X509ContentType.Pkcs12));
    }

    public static string DeploymentJson(int number, string status, string? stage, bool finished, bool succeeded, string? error = null) =>
        "{\"number\":" + number
        + ",\"status\":\"" + status + "\""
        + ",\"stage\":" + (stage is null ? "null" : "\"" + stage + "\"")
        + ",\"finished\":" + (finished ? "true" : "false")
        + ",\"succeeded\":" + (succeeded ? "true" : "false")
        + ",\"commit\":\"abc123\",\"commit_message\":\"Fix\",\"strategy\":\"rolling\",\"is_rollback\":false"
        + ",\"error\":" + (error is null ? "null" : "\"" + error + "\"")
        + ",\"created_at\":null,\"finished_at\":null,\"url\":\"https://x/api/v1/apps/shop/deployments/" + number + "\"}";

    public const string AppJson =
        "{\"slug\":\"shop\",\"name\":\"Shop\",\"status\":\"running\",\"stack\":\"go\",\"deploy_strategy\":\"stop_first\","
        + "\"domains\":[\"shop.example.com\"],\"live_deployment\":3,\"latest_deployment\":null,\"created_at\":\"2026-11-16T10:00:00+00:00\"}";
}
