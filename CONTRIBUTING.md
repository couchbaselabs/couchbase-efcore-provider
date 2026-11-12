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

### EF Core specification tests

`tests/Couchbase.EntityFrameworkCore.Specification.Tests` runs EF Core's own provider
specification suites (the tests every EF Core provider is checked against) on Couchbase. Many
of them fail today; `known-failures.txt` in that project lists every test expected to fail, and a
run passes when nothing outside that list fails. When a change makes listed tests pass, update
the list in the same PR so it only ever shrinks.

The suites need a Couchbase cluster with a bucket they can create scopes in. EF's transaction
suite needs a multi-node cluster: a single node hides the node-pinning that SQL++ transactions
depend on. Point the suites at a cluster and run them with the script, which starts one test
process per test class so a crash in one suite can't abort the rest:

```sh
export COUCHBASE_CONNECTION_STRING=couchbase://host1,host2,host3
export COUCHBASE_USERNAME=Administrator COUCHBASE_PASSWORD=password COUCHBASE_BUCKET=default
tests/Couchbase.EntityFrameworkCore.Specification.Tests/scripts/run-suites.sh
```

It prints new failures and tests that now pass, and exits non-zero on new failures. Pass class
name fragments to run part of the suite (`run-suites.sh Northwind GearsOfWar`), and
`--update-baseline` after a full run to rewrite `known-failures.txt`. Each suite keeps its data in
its own scope, reused between runs; set `COUCHBASE_SPEC_RESET_SCOPES=1` to recreate them, for
example after changing a test model. Without `COUCHBASE_CONNECTION_STRING`, the tests start the
Aspire `AppHost` container instead, like the integration tests.

A few harness classes emulate provider fixes so that suites can seed and run at all; each is
marked "Test-harness only" and says which provider bug it works around. Remove the emulation
when the provider fix lands.

### Updating `packages.lock.json` after a version bump

Every project restores with `RestorePackagesWithLockFile=true` (set in `Directory.Build.props`),
which pins the full resolved dependency graph — including transitive packages — in a committed
`packages.lock.json` per project. CI restores with `--locked-mode`, so it fails the build if a
project's lock file doesn't match what NuGet would resolve.

Whenever you bump a version in `Directory.Packages.props` (or add/remove a `PackageReference`),
regenerate the affected lock file(s) and commit them alongside the change:

```sh
dotnet restore couchbase-dotnet-ef.sln --force-evaluate
```

`--force-evaluate` is required — a plain `dotnet restore` no-ops once a lock file already exists
and matches the last-restored graph, so it won't pick up your version change on its own.

**Exception:** `tests/AppHost`, `samples/Contoso.AppHost`, and (via its project reference to
`AppHost`) `tests/Couchbase.EntityFrameworkCore.IntegrationTests` opt out of the lock file
(`RestorePackagesWithLockFile=false` in each `.csproj`) and have no `packages.lock.json`. Their
`Aspire.AppHost.Sdk` build dynamically adds a `PackageReference` to
`Aspire.Dashboard.Sdk.<RID>`/`Aspire.Hosting.Orchestration.<RID>` for whichever platform is doing
the restore, so the resolved graph — and any lock file generated from it — is tied to one OS/arch
and would break locked-mode restore on every other platform (e.g. CI's `ubuntu-latest` vs. a
macOS/arm64 contributor machine). Don't regenerate a lock file for these three projects.

Because `--locked-mode` sets `RestoreLockedMode=true` for the whole restore invocation, and NuGet
fails any project restored under that mode with no lock file to check (`NU1004`), and because
`dotnet restore` only accepts a single project or solution argument, CI restores each of the five
locked projects individually with `--locked-mode`, then each of the three opted-out projects
individually without it — two groups of per-project restores, not one `--locked-mode` restore of
the whole solution. See `.github/workflows/ci.yml`.

## Continuous integration

`.github/workflows/ci.yml` runs on every push/PR to `main`: it builds the whole solution in
`Release` configuration and runs the unit test suite. The integration tests and the EF Core
specification tests are not part of CI — they need a running cluster and take real time — so
they stay a manual/local gate before merging anything that touches provider internals.

## Cutting a release

`.github/workflows/release.yml` runs on manual dispatch (`workflow_dispatch`) against an existing
tag — it does **not** trigger automatically on tag push. (GitHub Actions' tag-filter glob syntax
combines `+`/`*` quantifiers with bracket expressions like `[0-9]` in ways that couldn't be
confirmed from the documentation, and a release trigger silently failing to fire is a bad failure
mode to risk — a sibling Couchbase repo, `couchbase-jvm-clients`, sidesteps the same problem the
same way.) To cut a release:

```sh
git tag 2.0.0-beta.3
git push origin 2.0.0-beta.3   # substitute your remote name for the upstream repo if not "origin"
gh workflow run release.yml --repo couchbaselabs/couchbase-efcore-provider -f tag=2.0.0-beta.3
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
