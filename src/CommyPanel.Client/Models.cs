using System.Text.Json.Serialization;

namespace CommyPanel;

/// <summary>What a token may do. The values are plain strings, so an ability added later does not break reading.</summary>
public static class Abilities
{
    public const string Read = "apps:read";
    public const string Deploy = "apps:deploy";
    public const string Control = "apps:control";
    public const string Logs = "apps:logs";
    public const string Env = "apps:env";
}

/// <summary>How a new release replaces the running one.</summary>
public static class DeployStrategies
{
    /// <summary>The new version starts beside the old one (no downtime).</summary>
    public const string Rolling = "rolling";

    /// <summary>The old version stops first, for software that cannot run twice on the same data.</summary>
    public const string StopFirst = "stop_first";
}

/// <summary>The token the client is using.</summary>
public sealed record Token
{
    [JsonPropertyName("name")] public string Name { get; init; } = "";

    [JsonPropertyName("abilities")] public IReadOnlyList<string> Abilities { get; init; } = Array.Empty<string>();

    /// <summary>The apps the token is limited to; null means every app its owner may use.</summary>
    [JsonPropertyName("apps")] public IReadOnlyList<string>? Apps { get; init; }

    [JsonPropertyName("expires_at")] public DateTimeOffset? ExpiresAt { get; init; }

    [JsonPropertyName("last_used_at")] public DateTimeOffset? LastUsedAt { get; init; }

    /// <summary>The owner's name.</summary>
    [JsonPropertyName("owner")] public string Owner { get; init; } = "";
}

/// <summary>One app, as the API describes it. It never holds secrets.</summary>
public sealed record App
{
    [JsonPropertyName("slug")] public string Slug { get; init; } = "";

    [JsonPropertyName("name")] public string Name { get; init; } = "";

    [JsonPropertyName("status")] public string Status { get; init; } = "";

    [JsonPropertyName("stack")] public string? Stack { get; init; }

    /// <summary>"rolling" or "stop_first" (see <see cref="DeployStrategies"/>).</summary>
    [JsonPropertyName("deploy_strategy")] public string DeployStrategy { get; init; } = DeployStrategies.Rolling;

    [JsonPropertyName("domains")] public IReadOnlyList<string> Domains { get; init; } = Array.Empty<string>();

    [JsonPropertyName("live_deployment")] public int? LiveDeployment { get; init; }

    [JsonPropertyName("latest_deployment")] public Deployment? LatestDeployment { get; init; }

    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>One deployment, rollback or "apply changes".</summary>
public sealed record Deployment
{
    [JsonPropertyName("number")] public int Number { get; init; }

    [JsonPropertyName("status")] public string Status { get; init; } = "";

    [JsonPropertyName("stage")] public string? Stage { get; init; }

    /// <summary>True once it is over, whatever the result. Poll until it is.</summary>
    [JsonPropertyName("finished")] public bool Finished { get; init; }

    /// <summary>
    /// Only meaningful once <see cref="Finished"/>. A watched release can still be rolled back later (status "rolled_back").
    /// </summary>
    [JsonPropertyName("succeeded")] public bool Succeeded { get; init; }

    [JsonPropertyName("commit")] public string? Commit { get; init; }

    [JsonPropertyName("commit_message")] public string? CommitMessage { get; init; }

    [JsonPropertyName("strategy")] public string Strategy { get; init; } = DeployStrategies.Rolling;

    [JsonPropertyName("is_rollback")] public bool IsRollback { get; init; }

    [JsonPropertyName("error")] public string? Error { get; init; }

    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; init; }

    [JsonPropertyName("finished_at")] public DateTimeOffset? FinishedAt { get; init; }

    [JsonPropertyName("url")] public string Url { get; init; } = "";

    /// <summary>The build log, only when asked for.</summary>
    [JsonPropertyName("log")] public string? Log { get; init; }
}

/// <summary>The output of an app's running container.</summary>
public sealed record LogOutput
{
    /// <summary>False when the app has no running container.</summary>
    [JsonPropertyName("live")] public bool Live { get; init; }

    [JsonPropertyName("lines")] public int Lines { get; init; }

    [JsonPropertyName("logs")] public string Logs { get; init; } = "";
}

/// <summary>The names of an app's environment variables. Values are never returned by the API.</summary>
public sealed record EnvKeys
{
    [JsonPropertyName("keys")] public IReadOnlyList<string> Keys { get; init; } = Array.Empty<string>();

    /// <summary>True when variables, volumes or links changed after the running container was made: call ApplyChangesAsync.</summary>
    [JsonPropertyName("pending_changes")] public bool PendingChanges { get; init; }
}

/// <summary>The answer to setting or removing a variable. The value is not part of it.</summary>
public sealed record EnvChange
{
    [JsonPropertyName("key")] public string Key { get; init; } = "";

    [JsonPropertyName("pending_changes")] public bool PendingChanges { get; init; }
}
