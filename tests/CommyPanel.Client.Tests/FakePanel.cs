using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CommyPanel.Tests;

/// <summary>A request the fake panel received.</summary>
internal sealed record Recorded(string Method, string Path, string Query, string Body, string Auth, string Accept, string UserAgent);

/// <summary>What the fake panel answers.</summary>
internal sealed record Reply(int Status, string Body = "", IReadOnlyDictionary<string, string>? Headers = null);

/// <summary>
/// A real HTTP (or HTTPS) server on a free loopback port that records every request and answers as the test says. The
/// handler gets the request and how many requests the server has seen so far (this one included).
/// </summary>
internal sealed class FakePanel : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly object _gate;
    private readonly List<Recorded> _requests;
    private int _disposed;

    private FakePanel(WebApplication app, string url, object gate, List<Recorded> requests)
    {
        _app = app;
        Url = url;
        _gate = gate;
        _requests = requests;
    }

    public string Url { get; }

    public IReadOnlyList<Recorded> Requests
    {
        get
        {
            lock (_gate)
            {
                return _requests.ToList();
            }
        }
    }

    public Recorded Last => Requests[^1];

    public static async Task<FakePanel> StartAsync(Func<Recorded, int, Reply> handler, X509Certificate2? certificate = null)
    {
        var gate = new object();
        var requests = new List<Recorded>();

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls((certificate is null ? "http" : "https") + "://127.0.0.1:0");

        if (certificate is not null)
        {
            // The slim builder leaves https out unless asked.
            builder.WebHost.UseKestrelHttpsConfiguration();
            builder.WebHost.ConfigureKestrel(kestrel => kestrel.ConfigureHttpsDefaults(https => https.ServerCertificate = certificate));
        }

        var app = builder.Build();

        app.Run(async context =>
        {
            string body;

            using (var reader = new StreamReader(context.Request.Body))
            {
                body = await reader.ReadToEndAsync();
            }

            var recorded = new Recorded(
                context.Request.Method,
                context.Request.Path.Value ?? "",
                (context.Request.QueryString.Value ?? "").TrimStart('?'),
                body,
                context.Request.Headers.Authorization.ToString(),
                context.Request.Headers.Accept.ToString(),
                context.Request.Headers.UserAgent.ToString());

            int count;

            lock (gate)
            {
                requests.Add(recorded);
                count = requests.Count;
            }

            var reply = handler(recorded, count);

            context.Response.StatusCode = reply.Status;
            context.Response.ContentType = "application/json";

            if (reply.Headers is not null)
            {
                foreach (var header in reply.Headers)
                {
                    context.Response.Headers[header.Key] = header.Value;
                }
            }

            await context.Response.WriteAsync(reply.Body);
        });

        await app.StartAsync();

        var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

        return new FakePanel(app, url, gate, requests);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
