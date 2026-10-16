# Preview packages

The **Preview packages** workflow publishes `Darp.Luau` prerelease packages to
`https://nuget.pkg.github.com/rosslight/index.json`. It runs for PRs from branches
in this repository targeting `main`, and can also be run manually. PRs from
other repositories do not publish packages. Stable releases remain on nuget.org.

The package includes the source generator, just like a release package.
PR versions use `<base>-pr.<PR number>.<run number>.<attempt>`; manual runs use
`<base>-preview.<run number>.<attempt>`. The base comes from `src/Directory.Build.props`.
After a successful publish, the workflow posts the version, feed and installation
command in a PR comment, updating that comment on subsequent publishes.
The version is also in the workflow run summary.

## Install

GitHub Packages requires authentication to download NuGet packages. Configure
the feed locally with a GitHub personal access token (classic) with
`read:packages` access. Keep the token out of checked-in configuration.

```powershell
dotnet nuget add source https://nuget.pkg.github.com/rosslight/index.json --name rosslight --username YOUR_GITHUB_USERNAME --password YOUR_READ_PACKAGES_TOKEN
dotnet add package Darp.Luau --version VERSION_FROM_RUN_SUMMARY
```

Keep nuget.org enabled for dependencies, including `Darp.Luau.Native`.
On Linux, .NET cannot encrypt source credentials; use `--store-password-in-clear-text`
only with a local configuration file outside your repository.

Alternatively, download the `preview-packages` artifact from the workflow run,
extract it, and use that directory as a local NuGet source. The artifact includes
the `.nupkg` package and `.snupkg` symbols.

## Publishing permissions

The PR build has read-only repository access. A separate job downloads its
package and publishes it with the workflow's `GITHUB_TOKEN`; it does not
check out or execute PR source. No additional publishing secret is required.
The comment job has permission to update the PR comment.

Because `pull_request_target` runs the workflow from the base branch, automatic
PR previews start after the workflow is merged into `main`.
