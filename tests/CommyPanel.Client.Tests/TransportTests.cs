using static CommyPanel.Tests.TestKit;

namespace CommyPanel.Tests;

public class TransportTests
{
    [Fact]
    public async Task RedirectsAreNeverFollowedSoTheTokenStaysHome()
    {
        await using var elsewhere = await FakePanel.StartAsync(Always(200, "{\"data\":[]}"));
        await using var panel = await FakePanel.StartAsync((_, _) => new Reply(302, "", new Dictionary<string, string> { ["Location"] = elsewhere.Url + "/api/v1/apps" }));
        using var client = Client(panel);

        await Assert.ThrowsAsync<RedirectException>(() => client.ListAppsAsync());

        Assert.Empty(elsewhere.Requests);
        Assert.Single(panel.Requests);
    }

    [Fact]
    public async Task AReadIsRetriedAfterAGatewayError()
    {
        await using var panel = await FakePanel.StartAsync((_, n) => n < 3 ? new Reply(503, "{\"error\":\"down\"}") : new Reply(200, "{\"data\":[]}"));
        var waits = new List<TimeSpan>();
        using var client = Client(panel, waits: waits);

        await client.ListAppsAsync();

        Assert.Equal(3, panel.Requests.Count);
        Assert.Equal(new[] { TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1000) }, waits);
    }

    [Fact]
    public async Task AWriteIsNotRetriedAfterAGatewayErrorBecauseItMayHaveHappened()
    {
        await using var panel = await FakePanel.StartAsync(Always(503, "{\"error\":\"down\"}"));
        using var client = Client(panel);

        await Assert.ThrowsAsync<ApiException>(() => client.StartDeploymentAsync("shop"));

        Assert.Single(panel.Requests);
    }

    [Fact]
    public async Task ARefusalForTooManyCallsIsRetriedForWritesToo_AfterTheWaitItAsksFor()
    {
        await using var panel = await FakePanel.StartAsync((_, n) => n == 1
            ? new Reply(429, "{\"error\":\"slow down\"}", new Dictionary<string, string> { ["Retry-After"] = "7" })
            : new Reply(202, "{\"data\":" + DeploymentJson(1, "queued", null, false, false) + "}"));
        var waits = new List<TimeSpan>();
        using var client = Client(panel, waits: waits);

        var deployment = await client.StartDeploymentAsync("shop");

        Assert.Equal(1, deployment.Number);
        Assert.Equal(new[] { TimeSpan.FromSeconds(7) }, waits);
        Assert.Equal(2, panel.Requests.Count);
    }

    [Fact]
    public async Task AnEnormousRetryAfterIsCapped()
    {
        await using var panel = await FakePanel.StartAsync((_, _) => new Reply(429, "{\"error\":\"slow down\"}", new Dictionary<string, string> { ["Retry-After"] = "86400" }));
        var waits = new List<TimeSpan>();
        using var client = Client(panel, o => o.MaxRetries = 1, waits);

        var exception = await Assert.ThrowsAsync<ApiException>(() => client.ListAppsAsync());

        Assert.True(exception.IsRateLimited);
        Assert.Equal(86400, exception.RetryAfterSeconds);
        Assert.Equal(new[] { TimeSpan.FromSeconds(30) }, waits);
    }

    [Fact]
    public async Task RetriesStopAtTheLimitAndACancelledTokenStopsTheCall()
    {
        await using var panel = await FakePanel.StartAsync(Always(503, "{\"error\":\"down\"}"));
        using var client = Client(panel, o => o.MaxRetries = 1);

        await Assert.ThrowsAsync<ApiException>(() => client.ListAppsAsync());
        Assert.Equal(2, panel.Requests.Count);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ListAppsAsync(cancelled.Token));
    }

    [Fact]
    public async Task AnAnswerThatIsTooLargeIsRefused()
    {
        await using var panel = await FakePanel.StartAsync(Always(200, "{\"data\":{\"live\":true,\"lines\":10,\"logs\":\"" + new string('x', 5000) + "\"}}"));
        using var client = Client(panel, o => o.MaxResponseBytes = 1000);

        await Assert.ThrowsAsync<ResponseTooLargeException>(() => client.LogsAsync("shop", 10));
    }

    [Fact]
    public async Task TheTokenNeverShowsUp()
    {
        await using var panel = await FakePanel.StartAsync(Always(200, "{\"data\":[]}"));
        using var client = Client(panel);

        foreach (var shown in new[] { client.ToString(), $"{client}", string.Format("{0}", client) })
        {
            Assert.DoesNotContain("secretsecret", shown);
            Assert.Contains("redacted", shown);
        }

        // A failed connection must not repeat it either.
        var url = panel.Url;
        await panel.DisposeAsync();

        using var orphan = new CommyPanelClient(url, TestKit.Token, new CommyPanelClientOptions { AllowHttpLoopback = true, MaxRetries = 0 });
        var exception = await Assert.ThrowsAsync<CommyPanelException>(() => orphan.ListAppsAsync());

        Assert.DoesNotContain("secretsecret", exception.Message);
        Assert.DoesNotContain("secretsecret", exception.ToString());
    }

    [Fact]
    public async Task NamesThatCannotBeRightNeverBecomeRequests()
    {
        await using var panel = await FakePanel.StartAsync(Always(200, "{\"data\":{}}"));
        using var client = Client(panel);

        var slugs = new[] { "", "../etc", "a/b", "A", "a b", "-x", "a?x=1", "a#b", "a%2Fb", new string('a', 64), "shop\n" };
        var keys = new[] { "", "1BAD", "A-B", "A B", "A/B", "A?", new string('A', 129), "KEY\n" };

        foreach (var slug in slugs)
        {
            await Assert.ThrowsAsync<InvalidInputException>(() => client.GetAppAsync(slug));
        }

        foreach (var key in keys)
        {
            await Assert.ThrowsAsync<InvalidInputException>(() => client.SetEnvAsync("shop", key, "v"));
            await Assert.ThrowsAsync<InvalidInputException>(() => client.UnsetEnvAsync("shop", key));
        }

        await Assert.ThrowsAsync<InvalidInputException>(() => client.GetDeploymentAsync("shop", 0));
        await Assert.ThrowsAsync<InvalidInputException>(() => client.GetDeploymentAsync("shop", -1));

        Assert.Empty(panel.Requests);
    }

    [Fact]
    public async Task WaitForDeploymentPollsUntilItIsOver()
    {
        await using var panel = await FakePanel.StartAsync((_, n) => n < 3
            ? new Reply(200, "{\"data\":" + DeploymentJson(4, "building", "build", false, false) + "}")
            : new Reply(200, "{\"data\":" + DeploymentJson(4, "active", "done", true, true) + "}"));
        var waits = new List<TimeSpan>();
        using var client = Client(panel, waits: waits);
        var looks = 0;

        var deployment = await client.WaitForDeploymentAsync("shop", 4, TimeSpan.FromSeconds(1), _ => looks++);

        Assert.True(deployment.Finished);
        Assert.True(deployment.Succeeded);
        Assert.Equal(3, looks);
        Assert.Equal(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1) }, waits);
    }

    [Fact]
    public async Task WaitForDeploymentUsesTheDefaultForATooShortInterval_AndStopsWhenCancelled()
    {
        await using var panel = await FakePanel.StartAsync(Always(200, "{\"data\":" + DeploymentJson(4, "building", "build", false, false) + "}"));
        using var source = new CancellationTokenSource();
        var waits = new List<TimeSpan>();
        using var client = Client(panel, o =>
        {
            o.Delay = (span, cancel) =>
            {
                waits.Add(span);
                source.Cancel();
                cancel.ThrowIfCancellationRequested();

                return Task.CompletedTask;
            };
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.WaitForDeploymentAsync("shop", 4, TimeSpan.FromMilliseconds(10), null, source.Token));

        Assert.Equal(new[] { TimeSpan.FromSeconds(3) }, waits);
    }
}
