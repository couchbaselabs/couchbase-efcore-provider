# Contributing

## Building and testing locally

```sh
dotnet restore couchbase-dotnet-ef.sln
dotnet build couchbase-dotnet-ef.sln --configuration Release --no-restore
dotnet test tests/Couchbase.EntityFrameworkCore.UnitTests/Couchbase.EntityFrameworkCore.UnitTests.csproj --configuration Release --no-build
```

The unit tests above don't need a Couchbase cluster. The integration tests
(`tests/Couchbase.EntityFrameworkCore.IntegrationTests`) spin up a real, local,
containerized Couchbase cluster via [Aspire](https://learn.microsoft.com/dotnet/aspire/) and
require Docker to be running:

```sh
dotnet test tests/Couchbase.EntityFrameworkCore.IntegrationTests/Couchbase.EntityFrameworkCore.IntegrationTests.csproj --configuration Release --no-build
```

## Continuous integration

`.github/workflows/ci.yml` runs on every push/PR to `main`: it builds the whole solution in
`Release` configuration and runs the unit test suite. The integration tests are not part of CI —
they need Docker-in-runner and take real time to spin up a cluster — so they stay a manual/local
gate before merging anything that touches provider internals.

## Cutting a release

`.github/workflows/release.yml` runs on manual dispatch (`workflow_dispatch`) against an existing
tag — it does **not** trigger automatically on tag push. (GitHub Actions' tag-filter glob syntax
combines `+`/`*` quantifiers with bracket expressions like `[0-9]` in ways that couldn't be
confirmed from the documentation, and a release trigger silently failing to fire is a bad failure
mode to risk — a sibling Couchbase repo, `couchbase-jvm-clients`, sidesteps the same problem the
same way.) To cut a release:

```sh
git tag 2.0.0-beta.2
git push origin 2.0.0-beta.2   # substitute your remote name for the upstream repo if not "origin"
gh workflow run release.yml --repo couchbaselabs/couchbase-efcore-provider -f tag=2.0.0-beta.2
```

(Or via the GitHub UI: Actions → Release → Run workflow, entering the tag name.) The workflow
checks out that tag, builds and strong-name-signs `Couchbase.EntityFrameworkCore`, validates the
public API surface against the last released version (`PackageValidationBaselineVersion` in the
`.csproj`), and uploads the resulting `.nupkg` as a workflow artifact. It does **not** publish to
NuGet.org — download the artifact and run `dotnet nuget push` yourself once you're satisfied with
it.

### One-time setup: the `SIGNING_KEY` secret

`Couchbase.snk` (the strong-name key file) is gitignored and only exists locally — it's never
committed. The release workflow needs it as a base64-encoded repository secret named
`SIGNING_KEY`:

```sh
# From a checkout that has the real Couchbase.snk in src/Couchbase.EntityFrameworkCore/:
base64 -i src/Couchbase.EntityFrameworkCore/Couchbase.snk | pbcopy   # macOS
# base64 -w0 src/Couchbase.EntityFrameworkCore/Couchbase.snk | xclip -selection clipboard   # Linux
```

Then in the GitHub repo: **Settings → Secrets and variables → Actions → New repository secret**,
name it `SIGNING_KEY`, and paste the base64 output as the value.

Without this secret set, `release.yml` fails fast with a clear error rather than silently
producing an unsigned package.

### Why the main project builds fine with or without the key

Signing is opt-in via an explicit `SignRelease=true` MSBuild property — **not** inferred from
whether `Couchbase.snk` happens to exist on disk. Some local checkouts keep a copy of the key
around outside of a release (e.g. for other tooling), so a normal contributor build, `dotnet test`
run, or CI run always stays unsigned regardless of that file's presence — you'd have to pass
`-p:SignRelease=true` explicitly to sign, which only `release.yml` does. When `SignRelease=true`
*is* passed, a `SIGNED` compile constant is defined, which `AssemblyInfo.cs` uses to omit its
`InternalsVisibleTo` declarations for that build — a strong-name-signed assembly requires a public
key on every `InternalsVisibleTo` target, and the test assemblies are never signed, so the two are
mutually exclusive. This only matters for the release workflow, which packs the main library
standalone and never builds the test projects against the signed output.

### After a release ships

Bump `PackageValidationBaselineVersion` in the main `.csproj` to the version that just shipped, so
the *next* release's `ValidateApiCompat` run compares against it instead of an older baseline.
