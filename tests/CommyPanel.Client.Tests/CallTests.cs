using static CommyPanel.Tests.TestKit;

namespace CommyPanel.Tests;

public class CallTests
{
    private sealed record Case(string Name, int Status, string Reply, Func<CommyPanelClient, Task> Call, string Method, string Path, string Query = "", string Body = "");

    private static IEnumerable<Case> Cases()
    {
        var deployment = "{\"data\":" + DeploymentJson(4, "queued", null, false, false) + "}";
        var app = "{\"data\":" + AppJson + "}";

        yield return new("token", 200, "{\"data\":{\"name\":\"ci\",\"abilities\":[],\"apps\":null,\"expires_at\":null,\"last_used_at\":null,\"owner\":\"Sam\"}}", c => c.TokenAsync(), "GET", "/api/v1/token");
        yield return new("list apps", 200, "{\"data\":[]}", c => c.ListAppsAsync(), "GET", "/api/v1/apps");
        yield return new("get app", 200, app, c => c.GetAppAsync("shop"), "GET", "/api/v1/apps/shop");
        yield return new("list deployments", 200, "{\"data\":[]}", c => c.ListDeploymentsAsync("shop", 5), "GET", "/api/v1/apps/shop/deployments", "limit=5");
        yield return new("list deployments, default limit", 200, "{\"data\":[]}", c => c.ListDeploymentsAsync("shop"), "GET", "/api/v1/apps/shop/deployments");
        yield return new("get deployment with log", 200, deployment, c => c.GetDeploymentAsync("shop", 4, true), "GET", "/api/v1/apps/shop/deployments/4", "log=1");
        yield return new("get deployment", 200, deployment, c => c.GetDeploymentAsync("shop", 4), "GET", "/api/v1/apps/shop/deployments/4");
        yield return new("start deployment", 202, deployment, c => c.StartDeploymentAsync("shop"), "POST", "/api/v1/apps/shop/deployments");
        yield return new("apply", 202, deployment, c => c.ApplyChangesAsync("shop"), "POST", "/api/v1/apps/shop/apply");
        yield return new("start app", 200, app, c => c.StartAppAsync("shop"), "POST", "/api/v1/apps/shop/start");
        yield return new("stop app", 200, app, c => c.StopAppAsync("shop"), "POST", "/api/v1/apps/shop/stop");
        yield return new("restart app", 200, app, c => c.RestartAppAsync("shop"), "POST", "/api/v1/apps/shop/restart");
        yield return new("logs", 200, "{\"data\":{\"live\":true,\"lines\":50,\"logs\":\"hi\"}}", c => c.LogsAsync("shop", 50), "GET", "/api/v1/apps/shop/logs", "tail=50");
        yield return new("list env", 200, "{\"data\":{\"keys\":[\"A\"],\"pending_changes\":false}}", c => c.ListEnvAsync("shop"), "GET", "/api/v1/apps/shop/env");
        yield return new("set env", 201, "{\"data\":{\"key\":\"A_B\",\"pending_changes\":true}}", c => c.SetEnvAsync("shop", "A_B", "va\"lue"), "PUT", "/api/v1/apps/shop/env/A_B", "", "{\"value\":\"va\\u0022lue\"}");
        yield return new("unset env", 200, "{\"data\":{\"key\":\"A_B\",\"pending_changes\":true}}", c => c.UnsetEnvAsync("shop", "A_B"), "DELETE", "/api/v1/apps/shop/env/A_B");
    }

    [Fact]
    public async Task EveryCallUsesTheRightMethodPathQueryAndBody()
    {
        foreach (var testCase in Cases())
        {
            await using var panel = await FakePanel.StartAsync(Always(testCase.Status, testCase.Reply));
            using var client = Client(panel);

            await testCase.Call(client);

            var request = panel.Last;
            Assert.True(
                request.Method == testCase.Method && request.Path == testCase.Path && request.Query == testCase.Query,
                $"{testCase.Name}: got {request.Method} {request.Path}?{request.Query}, wanted {testCase.Method} {testCase.Path}?{testCase.Query}");

            // JSON may escape a quote as " or \", both mean the same: compare what was meant.
            if (testCase.Body.Length > 0)
            {
                Assert.Equal(System.Text.Json.JsonDocument.Parse(testCase.Body).RootElement.GetProperty("value").GetString(),
                    System.Text.Json.JsonDocument.Parse(request.Body).RootElement.GetProperty("value").GetString());
            }
            else
            {
                Assert.True(request.Body.Length == 0, $"{testCase.Name}: unexpected body {request.Body}");
            }
        }
    }

    [Fact]
    public async Task EveryRequestCarriesTheTokenOnlyInTheHeader()
    {
        await using var panel = await FakePanel.StartAsync(Always(200, "{\"data\":{\"name\":\"ci\",\"abilities\":[\"apps:read\"],\"apps\":null,\"expires_at\":null,\"last_used_at\":null,\"owner\":\"Sam\"}}"));
        using var client = Client(panel, o => o.UserAgentPrefix = "mytool/1.0");

        var token = await client.TokenAsync();

        var request = panel.Last;
        Assert.Equal("Bearer " + TestKit.Token, request.Auth);
        Assert.Equal("application/json", request.Accept);
        Assert.StartsWith("mytool/1.0 commypanel-dotnet/", request.UserAgent);
        Assert.DoesNotContain("secretsecret", request.Path + request.Query + request.Body);

        Assert.Equal("ci", token.Name);
        Assert.Equal("Sam", token.Owner);
        Assert.Null(token.Apps);
        Assert.Equal(new[] { Abilities.Read }, token.Abilities);
    }

    [Fact]
    public async Task DecodesTheObjects()
    {
        var latest = DeploymentJson(3, "active", "done", true, true).Replace("\"created_at\":null,\"finished_at\":null", "\"created_at\":\"2026-11-16T10:00:00+00:00\",\"finished_at\":\"2026-11-16T10:01:00+00:00\"");
        var json = AppJson.Replace("\"latest_deployment\":null", "\"latest_deployment\":" + latest);
        await using var panel = await FakePanel.StartAsync(Always(200, "{\"data\":" + json + ",\"unknown_future_field\":1}"));
        using var client = Client(panel);

        var app = await client.GetAppAsync("shop");

        Assert.Equal(DeployStrategies.StopFirst, app.DeployStrategy);
        Assert.Equal("go", app.Stack);
        Assert.Equal(3, app.LiveDeployment);
        Assert.Equal(new[] { "shop.example.com" }, app.Domains);

        var deployment = app.LatestDeployment;
        Assert.NotNull(deployment);
        Assert.True(deployment!.Finished);
        Assert.True(deployment.Succeeded);
        Assert.Equal("abc123", deployment.Commit);
        Assert.Equal(DeployStrategies.Rolling, deployment.Strategy);
        Assert.Equal(TimeSpan.FromMinutes(1), deployment.FinishedAt - deployment.CreatedAt);
    }

    [Theory]
    [InlineData(201, true)]
    [InlineData(200, false)]
    public async Task SetEnvTellsNewFromReplaced(int status, bool wantCreated)
    {
        await using var panel = await FakePanel.StartAsync(Always(status, "{\"data\":{\"key\":\"A\",\"pending_changes\":true}}"));
        using var client = Client(panel);

        var (change, created) = await client.SetEnvAsync("shop", "A", "v");

        Assert.Equal(wantCreated, created);
        Assert.Equal("A", change.Key);
        Assert.True(change.PendingChanges);
    }
}
