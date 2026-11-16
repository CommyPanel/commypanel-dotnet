# CommyPanel.Client

A small .NET client for the HTTP API of a [CommyPanel](https://github.com/commypanel/panel) server: list apps,
start and follow deployments, start, stop and restart an app, read its logs and manage its environment variables.

- **No dependencies.** Only `System.Net.Http` and `System.Text.Json`.
- **Hard to misuse.** https only, no redirects, bounded retries and answers, and the token kept out of errors,
  logs and the debugger. See [Security](#security).
- **Matches the API.** `testdata/openapi.yaml` is a copy of the panel's API description, and a test fails when the
  API gains a call this library does not have.

```bash
dotnet add package CommyPanel.Client
```

Targets `net8.0` and later.

## Use

Create a token in the panel under **Settings > API tokens**. Give it only what it needs (for a deploy job:
`apps:deploy` and `apps:read`), limit it to one app, and let it expire.

```csharp
using CommyPanel;

using var client = new CommyPanelClient(
    "https://panel.example.com",
    Environment.GetEnvironmentVariable("COMMY_TOKEN")!);

var queued = await client.StartDeploymentAsync("shop");

var result = await client.WaitForDeploymentAsync(
    "shop",
    queued.Number,
    pollInterval: TimeSpan.FromSeconds(3),
    onUpdate: d => Console.WriteLine(d.Status));

if (!result.Succeeded)
{
    throw new Exception($"The deployment ended as {result.Status}");
}
```

Errors from the panel are `ApiException`s with the HTTP `Status`, the panel's own sentence in `Detail`, the field
problems of a 422 in `Fields`, and the running deployment of a 409 in `Deployment`. Test them with `IsNotFound`,
`IsUnauthorized`, `IsForbidden`, `IsConflict` and `IsRateLimited`. Everything this library raises derives from
`CommyPanelException`. Every method takes a `CancellationToken`.

| Method | API call | Ability |
|---|---|---|
| `TokenAsync` | `GET /token` | none |
| `ListAppsAsync`, `GetAppAsync` | `GET /apps`, `GET /apps/{slug}` | `apps:read` |
| `ListDeploymentsAsync`, `GetDeploymentAsync` | `GET /apps/{slug}/deployments[/{n}]` | `apps:read` |
| `StartDeploymentAsync`, `ApplyChangesAsync` | `POST /apps/{slug}/deployments`, `/apply` | `apps:deploy` |
| `StartAppAsync`, `StopAppAsync`, `RestartAppAsync` | `POST /apps/{slug}/start`, `/stop`, `/restart` | `apps:control` |
| `LogsAsync` | `GET /apps/{slug}/logs` | `apps:logs` |
| `ListEnvAsync`, `SetEnvAsync`, `UnsetEnvAsync` | `GET`, `PUT`, `DELETE /apps/{slug}/env[/{key}]` | `apps:env` |
| `WaitForDeploymentAsync` | polls `GET .../deployments/{n}` | `apps:read` |

Environment variable **values are write-only**: the API never returns them, so `ListEnvAsync` gives names, and
`SetEnvAsync` returns `(change, created)`.

## Security

- **https only.** The constructor refuses any other address. `AllowHttpLoopback` allows plain http for `localhost`,
  `127.0.0.1` and `::1` only, for development.
- **TLS is always verified.** There is no option to skip it. For a panel with a private certificate authority, set
  `CaCertificatePem`: the certificate must still chain to that authority (or the system's), be valid now and be
  for the host you asked for.
- **Redirects are never followed**, so the token cannot be handed to another address; a redirect is a
  `RedirectException`.
- **The token stays out of sight.** It is only ever in the `Authorization` header: not in URLs, exception messages,
  `ToString()` or the debugger. Text the panel sends back is cleaned of it too, in case a misbehaving server echoes it.
- **Bounded.** A timeout (30 s, answer included), at most 2 retries (never for a write that may have happened: only
  after a 429, and gateway errors for reads), `Retry-After` capped at 30 s, answers capped at 16 MiB (read as a
  stream, so a huge answer is never buffered).
- **Names are checked before any request**, so an app or variable name cannot change the path.
- Text from the panel (logs, commit messages, error sentences) is **not under your control**. If you print it to a
  terminal, remove control characters first.

Report a security problem privately, as described in [SECURITY.md](SECURITY.md).

## Compatibility

This library speaks API `v1` (`/api/v1`) of CommyPanel 0.10 and later.

## Development

```bash
scripts/sync-spec.sh ../../commypanel   # copy the API description from the panel repository
dotnet build -c Release
dotnet test -c Release
```

The tests start a real local HTTP server (Kestrel) for the calls and a local HTTPS server for the certificate checks.
The certificates in `tests/fixtures/tls` are throwaway and for the tests only; `generate.sh` makes them again.

## Publishing

Publishing to NuGet (with an API key from CI) is a later, deliberate step, so there is no publish workflow yet.
When it comes: build in CI from a tag, publish with a scoped, expiring key, and check the package contents first
(`dotnet pack` and look inside the `.nupkg`).

MIT licensed.
