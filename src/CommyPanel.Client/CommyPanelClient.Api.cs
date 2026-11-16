namespace CommyPanel;

public sealed partial class CommyPanelClient
{
    private static string AppPath(string slug, params string[] rest)
    {
        Validation.Slug(slug);

        return "/apps/" + slug + string.Concat(rest.Select(part => "/" + part));
    }

    /// <summary>
    /// Describes the token this client uses: its name, abilities, the apps it is limited to and when it expires. Any
    /// valid token may ask. It is a good way to check a token when it is saved.
    /// </summary>
    public async Task<Token> TokenAsync(CancellationToken cancellationToken = default) =>
        (await SendAsync<Token>(HttpMethod.Get, "/token", null, null, cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>Lists the apps the token may see (ability apps:read).</summary>
    public async Task<IReadOnlyList<App>> ListAppsAsync(CancellationToken cancellationToken = default) =>
        (await SendAsync<List<App>>(HttpMethod.Get, "/apps", null, null, cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>Returns one app (ability apps:read).</summary>
    public async Task<App> GetAppAsync(string slug, CancellationToken cancellationToken = default) =>
        (await SendAsync<App>(HttpMethod.Get, AppPath(slug), null, null, cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>
    /// Returns the newest deployments of an app, newest first (ability apps:read). A limit of 0 uses the panel's default
    /// (20); the panel allows at most 50.
    /// </summary>
    public async Task<IReadOnlyList<Deployment>> ListDeploymentsAsync(string slug, int limit = 0, CancellationToken cancellationToken = default)
    {
        var path = AppPath(slug, "deployments");
        var query = limit > 0 ? new List<(string, string)> { ("limit", limit.ToString(System.Globalization.CultureInfo.InvariantCulture)) } : null;

        return (await SendAsync<List<Deployment>>(HttpMethod.Get, path, query, null, cancellationToken).ConfigureAwait(false)).Data;
    }

    /// <summary>Returns one deployment (ability apps:read), with the build log when <paramref name="withLog"/> is true.</summary>
    public async Task<Deployment> GetDeploymentAsync(string slug, int number, bool withLog = false, CancellationToken cancellationToken = default)
    {
        if (number < 1)
        {
            throw new InvalidInputException();
        }

        var path = AppPath(slug, "deployments", number.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var query = withLog ? new List<(string, string)> { ("log", "1") } : null;

        return (await SendAsync<Deployment>(HttpMethod.Get, path, query, null, cancellationToken).ConfigureAwait(false)).Data;
    }

    /// <summary>
    /// Queues a deployment (ability apps:deploy) and returns it at once; follow it with <see cref="WaitForDeploymentAsync"/>.
    /// If one is already running the error is a conflict (<see cref="ApiException.IsConflict"/>) whose
    /// <see cref="ApiException.Deployment"/> is the running one.
    /// </summary>
    public async Task<Deployment> StartDeploymentAsync(string slug, CancellationToken cancellationToken = default) =>
        (await SendAsync<Deployment>(HttpMethod.Post, AppPath(slug, "deployments"), null, null, cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>
    /// Makes changed variables, volumes and links take effect from the version that is already running, without a build
    /// (ability apps:deploy). Returns the queued deployment.
    /// </summary>
    public async Task<Deployment> ApplyChangesAsync(string slug, CancellationToken cancellationToken = default) =>
        (await SendAsync<Deployment>(HttpMethod.Post, AppPath(slug, "apply"), null, null, cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>Starts a stopped app (ability apps:control).</summary>
    public async Task<App> StartAppAsync(string slug, CancellationToken cancellationToken = default) =>
        (await SendAsync<App>(HttpMethod.Post, AppPath(slug, "start"), null, null, cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>Stops a running app; visitors see an error until it is started again (ability apps:control).</summary>
    public async Task<App> StopAppAsync(string slug, CancellationToken cancellationToken = default) =>
        (await SendAsync<App>(HttpMethod.Post, AppPath(slug, "stop"), null, null, cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>Restarts a running app (ability apps:control).</summary>
    public async Task<App> RestartAppAsync(string slug, CancellationToken cancellationToken = default) =>
        (await SendAsync<App>(HttpMethod.Post, AppPath(slug, "restart"), null, null, cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>
    /// Reads the last lines of the running container's output (ability apps:logs). <paramref name="tail"/> is clamped by
    /// the panel to 10..5000; 0 uses the default (200). Logs often hold secrets, and every read is recorded in the
    /// panel's audit log. The text is not cleaned: if you print it to a terminal, remove control characters first.
    /// </summary>
    public async Task<LogOutput> LogsAsync(string slug, int tail = 0, CancellationToken cancellationToken = default)
    {
        var path = AppPath(slug, "logs");
        var query = tail > 0 ? new List<(string, string)> { ("tail", tail.ToString(System.Globalization.CultureInfo.InvariantCulture)) } : null;

        return (await SendAsync<LogOutput>(HttpMethod.Get, path, query, null, cancellationToken).ConfigureAwait(false)).Data;
    }

    /// <summary>Lists the names of an app's environment variables (ability apps:env). The API never returns values.</summary>
    public async Task<EnvKeys> ListEnvAsync(string slug, CancellationToken cancellationToken = default) =>
        (await SendAsync<EnvKeys>(HttpMethod.Get, AppPath(slug, "env"), null, null, cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>
    /// Sets an environment variable (ability apps:env). <c>Created</c> is true when the variable is new. The change takes
    /// effect with <see cref="ApplyChangesAsync"/> or the next deployment. The value is sent, never read back.
    /// </summary>
    public async Task<(EnvChange Change, bool Created)> SetEnvAsync(string slug, string key, string value, CancellationToken cancellationToken = default)
    {
        Validation.EnvKey(key);
        ArgumentNullException.ThrowIfNull(value);

        var path = AppPath(slug, "env", key);
        var (status, change) = await SendAsync<EnvChange>(HttpMethod.Put, path, null, new Dictionary<string, string> { ["value"] = value }, cancellationToken).ConfigureAwait(false);

        return (change, status == 201);
    }

    /// <summary>Removes an environment variable (ability apps:env). A variable that is not there is a 404 (<see cref="ApiException.IsNotFound"/>).</summary>
    public async Task<EnvChange> UnsetEnvAsync(string slug, string key, CancellationToken cancellationToken = default)
    {
        Validation.EnvKey(key);

        return (await SendAsync<EnvChange>(HttpMethod.Delete, AppPath(slug, "env", key), null, null, cancellationToken).ConfigureAwait(false)).Data;
    }

    /// <summary>
    /// Polls a deployment until it is finished and returns the final state. <paramref name="pollInterval"/> is how often
    /// to look (default and minimum 3 and 1 seconds: anything under a second means 3); <paramref name="onUpdate"/>, when
    /// not null, is called with every look. Cancel the token to stop waiting; the deployment itself carries on in the
    /// panel. Check <see cref="Deployment.Succeeded"/> (and Status) on the result: a deployment can finish as failed or
    /// rolled back.
    /// </summary>
    public async Task<Deployment> WaitForDeploymentAsync(
        string slug,
        int number,
        TimeSpan? pollInterval = null,
        Action<Deployment>? onUpdate = null,
        CancellationToken cancellationToken = default)
    {
        var poll = pollInterval is { } given && given >= TimeSpan.FromSeconds(1) ? given : TimeSpan.FromSeconds(3);

        while (true)
        {
            var deployment = await GetDeploymentAsync(slug, number, false, cancellationToken).ConfigureAwait(false);
            onUpdate?.Invoke(deployment);

            if (deployment.Finished)
            {
                return deployment;
            }

            await _delay(poll, cancellationToken).ConfigureAwait(false);
        }
    }
}
