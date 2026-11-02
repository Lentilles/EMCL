# Publishing to NuGet

## Release contents

The version of all eight libraries is set by the `Version` property in `Directory.Build.props`.
The initial version is `0.1.0-preview.1`; override it with `-p:Version=...`.
The same file configures the author, copyright, tags, MIT license, package README,
and portable PDBs. Package IDs match project names; descriptions are in the `.csproj` files.
The SDK generates `.nuspec` files and package dependencies automatically.

Each library produces two files:

- `.nupkg`: the `net10.0` DLL, XML API documentation, `nuget/README.md` under the name
  `README.md`, the complete `LICENSE` text, and package metadata;
- `.snupkg`: portable PDBs for the NuGet symbol server.

Publish `EMCL`, `EMCL.Core`, `EMCL.Protocol`, `EMCL.Client`,
`EMCL.Objects`, `EMCL.Framing.Hdlc`, `EMCL.Framing.Wrapper`, and `EMCL.Transport`
with the same version. Examples and tests are not packaged.

## GitHub Actions and Trusted Publishing

The workflow [ci-cd.yml](../.github/workflows/ci-cd.yml) restores dependencies,
builds Release, runs tests, and packages all libraries on branch pushes and pull
requests. You can also run these checks manually from the Actions tab.
It saves `.nupkg` and `.snupkg` files as a workflow artifact for 14 days.

Pushing a tag such as `v0.1.0-preview.1` also starts the publication job after
the build succeeds. The tag without `v` becomes the version of all eight packages,
overriding `Directory.Build.props`. The publication job downloads the packages
from that same run and uses NuGet Trusted Publishing with GitHub OIDC.
Only this job has `id-token: write`; it runs in the `nuget` environment.

### One-time setup

1. Push this repository, including `.github/workflows/ci-cd.yml`, to GitHub.
2. In **Settings → Environments**, create an environment named `nuget`.
   If you configure deployment branch/tag rules, allow your `v*` release tags.
3. In **Settings → Secrets and variables → Actions → Variables**, create
   `NUGET_USER` with your NuGet.org account username (profile name, not email).
   This is an Actions variable; no stored NuGet API key is required.
4. Sign in to NuGet.org and open **Trusted Publishing** from your account menu.
   Add a GitHub policy with the following values:

   | Policy field | Value |
   | --- | --- |
   | Repository Owner | Your GitHub user or organization that owns the repository |
   | Repository | The actual GitHub repository name, for example `EMCL` |
   | Workflow File | `ci-cd.yml` (file name only) |
   | Environment | `nuget` |
   | Package owner | The NuGet user or organization that owns the packages |
   | Package scope | All eight EMCL package IDs; for example, the pattern `EMCL*` |

   Allow publishing new packages for the first release and new versions for later
   releases. The account in `NUGET_USER` must be able to use this policy for the
   chosen package owner. Check that all eight package IDs are available to you.

The policy's repository owner, repository, workflow file, and environment must
match this workflow's GitHub identity. `NuGet/login` exchanges the OIDC token for
a temporary API key immediately before uploading packages. See the official
[NuGet Trusted Publishing guide](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing).

### Publishing a release

Commit the release changes and push them to GitHub, then create and push a version tag:

```sh
git tag v0.1.0-preview.1
git push origin v0.1.0-preview.1
```

Follow **Actions → CI and NuGet publishing**. Once the build passes, the publication
job obtains its temporary credentials and uploads the eight library packages and
their symbol packages. Packages and symbols are uploaded separately so a rerun
can retry symbols even when the corresponding library version is already present.
Existing versions are skipped with `--skip-duplicate`.

Use a new version tag for each release, such as `v0.1.0-preview.2` or `v0.1.0`.
To retry a partially completed publication, rerun the failed jobs in the existing
tag workflow run. Branch pushes, pull requests, and manual workflow runs perform
checks and create artifacts without publishing.

## Preparation

A stable .NET SDK 10.0.100 or later in the 10.0 line is required. The script also requires
Bash (macOS, Linux, or Git Bash on Windows). Run from the repository root:

```sh
bash scripts/pack.sh 0.1.0-preview.1
```

The script restores dependencies, builds Release, runs tests, and creates
8 `.nupkg` and 8 `.snupkg` files in `packages/0.1.0-preview.1/`. Without an argument,
it reads the version from `Directory.Build.props`. The script only prepares files.

You can supply a public repository URL during preparation:

```sh
REPOSITORY_URL=https://your-host/your-account/EMCL \
  bash scripts/pack.sh 0.1.0-preview.1
```

Replace the URL with the actual address. It will be included in repository metadata and Project URL.
To set a permanent address, add the `RepositoryUrl` property to `Directory.Build.props`.
Source Link also requires a configured Git remote on a supported host and an accessible
published commit; setting `RepositoryUrl` alone does not enable the debugger to
download source files.

The equivalent steps without Bash (PowerShell, cmd, or another shell):

```sh
dotnet restore EMCL.slnx --disable-parallel --disable-build-servers -m:1 -p:Version=0.1.0-preview.1
dotnet build EMCL.slnx -c Release --no-restore --disable-build-servers -m:1 -p:Version=0.1.0-preview.1
dotnet test --project tests/EMCL.Tests/EMCL.Tests.csproj -c Release --no-build --no-restore
dotnet pack EMCL.slnx -c Release --no-restore --no-build --disable-build-servers -m:1 -p:Version=0.1.0-preview.1 -p:PackageOutputPath=../../packages/0.1.0-preview.1
```

In the final command, the relative `PackageOutputPath` is resolved against each
`src/<project name>/` directory, so all packages end up in one directory at the repository root.

## Package verification

Open each `.nupkg` as a ZIP archive and check the version, author, description, `MIT` license,
README, and dependencies in `.nuspec`. Internal EMCL dependencies must reference
the selected release version. Install `EMCL` into a separate `net10.0` project
from the local `packages/<version>/` source and check the example in the package README.

## Manual publishing

Create an account on [nuget.org](https://www.nuget.org/) and an API key with Push permission
for all eight package IDs. Before the first publication, check name availability
on NuGet.org. Each new package version must use a new version number;
an existing version cannot be replaced.

After verification, upload the prepared files. In Bash, with the API key already
supplied through the `NUGET_API_KEY` environment variable:

```sh
dotnet nuget push 'packages/0.1.0-preview.1/*.nupkg' \
  --source https://api.nuget.org/v3/index.json \
  --api-key "$NUGET_API_KEY" --symbol-api-key "$NUGET_API_KEY" \
  --skip-duplicate
```

Store the key in an environment variable or CI secrets. When matching `.snupkg`
files are alongside the packages, the NuGet CLI uploads the symbol packages as well.
`--skip-duplicate` allows retrying after a partial publication. After indexing, check
all eight package pages and installation of the selected version from NuGet.org.

Microsoft documentation: [creating a package](https://learn.microsoft.com/en-us/nuget/create-packages/creating-a-package-msbuild),
[symbol packages](https://learn.microsoft.com/en-us/nuget/create-packages/symbol-packages-snupkg),
[Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing),
[dotnet nuget push](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-push).
