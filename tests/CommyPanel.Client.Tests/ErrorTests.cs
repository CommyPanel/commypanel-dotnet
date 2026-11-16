using System.Text.Json;
using static CommyPanel.Tests.TestKit;

namespace CommyPanel.Tests;

public class ErrorTests
{
    [Theory]
    [InlineData(404, "{\"error\":\"Not Found\",\"message\":\"Not Found\"}")]
    [InlineData(401, "{\"error\":\"Unauthenticated.\"}")]
    [InlineData(403, "{\"error\":\"Invalid ability provided.\"}")]
    [InlineData(409, "{\"error\":\"A deployment is already in progress.\"}")]
    [InlineData(429, "{\"error\":\"Too Many Attempts.\"}")]
    public async Task ErrorsCarryThePanelsSentenceAndTheirKind(int status, string body)
    {
        await using var panel = await FakePanel.StartAsync(Always(status, body));
        using var client = Client(panel, o => o.MaxRetries = 0);

        var exception = await Assert.ThrowsAsync<ApiException>(() => client.GetAppAsync("shop"));

        Assert.Equal(status, exception.Status);
        Assert.False(string.IsNullOrEmpty(exception.Detail));
        Assert.Contains(exception.Detail, exception.Message);
        Assert.Equal(status == 404, exception.IsNotFound);
        Assert.Equal(status == 401, exception.IsUnauthorized);
        Assert.Equal(status == 403, exception.IsForbidden);
        Assert.Equal(status == 409, exception.IsConflict);
        Assert.Equal(status == 429, exception.IsRateLimited);
    }

    [Fact]
    public async Task AConflictNamesTheRunningDeployment()
    {
        var body = "{\"error\":\"A deployment is already in progress.\",\"deployment\":" + DeploymentJson(7, "building", "build", false, false) + "}";
        await using var panel = await FakePanel.StartAsync(Always(409, body));
        using var client = Client(panel);

        var exception = await Assert.ThrowsAsync<ApiException>(() => client.StartDeploymentAsync("shop"));

        Assert.True(exception.IsConflict);
        Assert.Equal(7, exception.Deployment?.Number);
    }

    [Fact]
    public async Task AValidationErrorNamesTheFields()
    {
        await using var panel = await FakePanel.StartAsync(Always(422, "{\"error\":\"PORT is set by CommyPanel.\",\"errors\":{\"key\":[\"PORT is set by CommyPanel.\"]}}"));
        using var client = Client(panel);

        var exception = await Assert.ThrowsAsync<ApiException>(async () => await client.SetEnvAsync("shop", "PORT", "1"));

        Assert.Equal(new[] { "PORT is set by CommyPanel." }, exception.Fields["key"]);
        Assert.Contains("key: PORT is set by CommyPanel.", exception.Message);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(502)]
    public async Task AnAnswerThatIsNotTheApisIsSaidPlainlyWithoutEchoingIt(int status)
    {
        await using var panel = await FakePanel.StartAsync((_, _) => new Reply(status, "<html>nginx says hi</html>"));
        using var client = Client(panel, o => o.MaxRetries = 0);

        var exception = await Assert.ThrowsAnyAsync<CommyPanelException>(() => client.ListAppsAsync());

        Assert.DoesNotContain("nginx says hi", exception.Message);
        Assert.DoesNotContain("nginx says hi", exception.ToString());
    }

    [Fact]
    public async Task TheTokenIsTakenOutOfWhatThePanelSays()
    {
        // A misbehaving (or impersonated) server could echo the Authorization header into its error text.
        var echoed = TestKit.Token;
        var body = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["error"] = "bad request from " + echoed,
            ["errors"] = new Dictionary<string, string[]> { ["field-" + echoed] = new[] { "wrong: " + echoed } },
            ["deployment"] = new Dictionary<string, object?>
            {
                ["number"] = 5,
                ["status"] = "building",
                ["finished"] = false,
                ["error"] = "failed with " + echoed,
                ["commit_message"] = "token " + echoed,
                ["log"] = "log " + echoed,
                ["url"] = "https://x/" + echoed,
            },
        });
        await using var panel = await FakePanel.StartAsync(Always(422, body));
        using var client = Client(panel);

        var exception = await Assert.ThrowsAsync<ApiException>(() => client.StartDeploymentAsync("shop"));

        Assert.Equal(422, exception.Status);
        Assert.Contains("[redacted]", exception.Message);
        Assert.DoesNotContain("secretsecret", exception.Message);
        Assert.DoesNotContain("secretsecret", exception.Detail);
        Assert.DoesNotContain("secretsecret", exception.ToString());

        Assert.All(exception.Fields, pair =>
        {
            Assert.DoesNotContain("secretsecret", pair.Key);
            Assert.All(pair.Value, sentence => Assert.DoesNotContain("secretsecret", sentence));
        });
        Assert.Contains(exception.Fields, pair => pair.Key.Contains("[redacted]") && pair.Value.Any(s => s.Contains("[redacted]")));

        var deployment = exception.Deployment;
        Assert.NotNull(deployment);
        Assert.Contains("[redacted]", deployment!.Error);
        Assert.Contains("[redacted]", deployment.CommitMessage);
        Assert.DoesNotContain("secretsecret", deployment.Error);
        Assert.DoesNotContain("secretsecret", deployment.CommitMessage);
        Assert.DoesNotContain("secretsecret", deployment.Log);
        Assert.DoesNotContain("secretsecret", deployment.Url);
    }
}
