# Tagged releases and Jellyfin repository publication

## Scope and prerequisites

The public repository URL is:

```text
https://raw.githubusercontent.com/everyonesoftware/collection-image-generator-jellyfin-plugin/repository/manifest.json
```

It is usable only after a successful first publication and anonymous verification. Users add it under **Jellyfin Dashboard → Plugins → Repositories**, install **Collection Image Generator** from **Catalog**, and restart Jellyfin. See the README for backups, artwork-preservation defaults, and the manual Docker fallback.

The first real publication must follow independent review of the workflows and release tools. Local tests and CI artifacts alone do not establish that a catalog feed is live. No publication or installation test may access a user's real library without separate authorization.

Requirements for maintainers:

- A public GitHub repository with Actions enabled. `GITHUB_TOKEN` needs `contents: write` in the publisher job; the workflow and reusable verification jobs otherwise have only `contents: read`.
- Branch rules must permit the publisher to create/update `repository` with an ordinary fast-forward push. Do not disable protections broadly to work around a failed run; review the failure first.
- The `repository` branch contains **`manifest.json` only**, plus an optional `README.md`. ZIPs and checksums belong in versioned release assets, never this branch. Existing unexpected files cause publication to stop.
- Never rewrite a published tag, overwrite release assets, delete previous manifest versions, or force-push repository history. GitHub release immutability can provide additional protection; workflow safety does not depend on that setting being enabled.

## Prepare a version

1. Update the explicit `<Version>`, `<AssemblyVersion>`, and `<FileVersion>` in `src/CollectionImageGenerator.Plugin/CollectionImageGenerator.Plugin.csproj` consistently. For package version `0.1.0`, assembly/file versions are `0.1.0.0`. Inspect other project versions when changing assembly identity, but do not edit generated release filenames by hand.
2. Confirm the Jellyfin package references/target ABI are intentional. The shared metadata CLI is authoritative:

   ```bash
   python tools/release_metadata.py --field version
   python tools/release_metadata.py --field assembly_version
   python tools/release_metadata.py --field archive_name
   python tools/release_metadata.py --field target_abi
   ```

3. Run the README's Release build, complete .NET/Python/Node tests, package generation, standalone ZIP loader, and manifest smoke check. Use only synthetic fixtures. Review the code and release workflow independently, then merge the intended release commit according to repository policy.
4. From that reviewed commit, create and push an annotated tag equal to `v` followed by the metadata version. These commands are **publication actions**, not a local dry run; execute them only when release publication is authorized:

   ```bash
   VERSION=$(python tools/release_metadata.py --field version)
   git tag -a "v$VERSION" -m "Collection Image Generator $VERSION"
   git push origin "refs/tags/v$VERSION"
   ```

Use increasing stable versions. The workflow publishes a non-prerelease and explicitly marks it latest; do not use this workflow for prereleases or older-version backfills. A tag/project-version mismatch stops before a release is created. Correct metadata in a new reviewed commit and use an appropriate new tag; never move a published release tag.

## What the tag workflow does

1. The `v*` tag push invokes the reusable `build.yml` workflow. Its **Linux and Windows** matrix must both succeed: restore/build, .NET tests including native rendering, Python packaging/release tests, Node settings tests, packaging, isolated packaged-loader probe, and generated-manifest smoke check. Both checkouts explicitly use the triggering ref.
2. The publisher downloads `collection-image-generator-Linux` from that same workflow run and verifies the archive's SHA-256 file. It **does not rebuild** the plugin. Package filenames come from `release_metadata.py`, not a hardcoded release number.
3. It refuses any existing release for that tag, including a draft. Failed API requests stop the run rather than implying that a release is absent. Asset upload never uses an overwrite/clobber option.
4. It checks the remote `repository` branch with `git ls-remote`. **Only exit status zero with empty output means the branch is absent.** Authentication/network errors stop publication. For an existing branch, fetch and `git show` must succeed; a missing/malformed manifest is an error. Only a genuinely absent branch bootstraps a previous manifest of `[]`.
5. `generate_manifest.py` validates the tag, project metadata, and ZIP; calculates Jellyfin's required **MD5** archive checksum; preserves previous versions; and refuses changed bytes for an existing version. The candidate includes the plugin GUID `f8a5df17-f0b4-4eab-9144-a46ea55c2b28`, assembly version, target ABI, and a versioned release ZIP URL. Its default timestamp is UTC. SHA-256 is separately supplied for manual archive checks.
6. It creates a **draft** release containing exactly the tested ZIP, its `.zip.sha256`, and the candidate `manifest.json`, then reads back the draft asset list. It publishes the release as stable/latest and checks its published state.
7. It anonymously verifies the **versioned release manifest and referenced ZIP**, including manifest shape and MD5, before advertising them in the stable repository. Public download failures are retried finitely; failure leaves the branch untouched.
8. A separate Git worktree updates only `manifest.json` on the metadata-only `repository` branch. An existing branch keeps its commit history; a new branch starts orphaned. The push is never forced, so a conflicting manual change stops safely. The workflow reads back the remote commit and anonymously verifies the stable raw URL with bounded retries for caching.

All tags share concurrency group `plugin-repository-publication`, with `cancel-in-progress: false`. This prevents an active publication being canceled by a newer tag and serializes branch writers. **Push one release tag at a time and wait for completion.** GitHub concurrency is not a durable FIFO queue: only one pending run is retained, and newer pending runs may replace older ones. Inspect any canceled run before manually rerunning it.

A green release run means both OS gates and public download/feed verification passed. It does not mean a real Jellyfin instance has loaded the plugin; a disposable-server installation check is separate.

## Immutable assets, history, and reruns

Treat published releases, ZIP bytes, checksums, versioned manifest snapshots, and tags as immutable. New versions append history to the stable manifest; old manifest entries keep their original versioned download URLs. Never replace an old ZIP to fix a bad build: bump versions and publish a new reviewed tag.

The workflow deliberately refuses to resume an existing release automatically. Rerunning a failed publisher can fail with `Release ... already exists`; this is a safety stop, not a reason to delete the release or use `gh release upload --clobber`. Inspect exactly which step failed, the release state and assets, the tag commit, and the current branch before taking manual action.

### Failure before any release exists

Resolve the underlying error first. For branch/API/network errors, recheck connectivity and the exact remote state. A confirmed absent release and unchanged tag may be rerun after review. Never infer branch absence from a timeout, fetch failure, HTTP error, missing file, or malformed JSON, and never bootstrap over an existing branch.

### Draft created but upload/publication failed

Leave the draft intact while investigating. Download its present assets and compare them with the verified artifact from the original workflow run. Do not rebuild and silently substitute bytes. A maintainer may complete missing assets without overwriting any existing one, after validating all three intended assets and the candidate manifest, then publish the verified draft as stable/latest. If anything is uncertain or bytes are wrong, stop and prepare a new version rather than mutate a published release. Any draft deletion/recreation requires a separate explicit maintainer decision; the automation never does it.

After a draft is published manually, follow the recovery below; a workflow rerun will still refuse the existing release.

### Release published but repository push failed

The release can be valid while Jellyfin's stable feed still lists only older versions. **Keep the release and all assets unchanged.** Do not rerun the entire publisher expecting an overwrite or reset. Use a clean checkout of the exact released tag and an authenticated maintainer shell (Bash examples below):

1. Inspect the release and download its existing assets without replacing local files:

   ```bash
   VERSION=$(python tools/release_metadata.py --field version)
   ASSEMBLY_VERSION=$(python tools/release_metadata.py --field assembly_version)
   ARCHIVE_NAME=$(python tools/release_metadata.py --field archive_name)
   TAG="v$VERSION"
   gh release view "$TAG" --repo everyonesoftware/collection-image-generator-jellyfin-plugin
   # Use a new empty local recovery directory, not an older build's artifacts.
   mkdir recovery
   gh release download "$TAG" --repo everyonesoftware/collection-image-generator-jellyfin-plugin --dir recovery
   (cd recovery && sha256sum --check "$ARCHIVE_NAME.sha256")
   python tools/verify_repository.py --manifest-url "https://github.com/everyonesoftware/collection-image-generator-jellyfin-plugin/releases/download/$TAG/manifest.json" --version "$ASSEMBLY_VERSION"
   ```

   The verifier makes anonymous requests; the authenticated `gh` commands above are for inspection/download. Stop if shape, version, MD5, ZIP, or SHA-256 checks fail.

2. Read the current branch state **again**, without suppressing errors. Run the following with Bash error checking enabled (`set -euo pipefail`):

   ```bash
   REMOTE_REF=$(git ls-remote --heads origin refs/heads/repository)
   if [[ -n "$REMOTE_REF" ]]; then
     git fetch --no-tags origin refs/heads/repository:refs/remotes/origin/repository
     REPOSITORY_BASE=$(git rev-parse refs/remotes/origin/repository)
     git show "$REPOSITORY_BASE:manifest.json" > recovery/previous-manifest.json
     git ls-tree -r --name-only "$REPOSITORY_BASE"
   else
     REPOSITORY_BASE=
     printf '[]\n' > recovery/previous-manifest.json
   fi
   ```

   Check that the branch contains only `manifest.json` and optional `README.md`. Review any divergence since the failed run. If the branch already contains this version, verify its archive/checksum and public URL before deciding whether any change is needed. Do not remove newer versions to restore an older candidate snapshot.

3. Generate a **new local stable-feed candidate** from the downloaded immutable ZIP and the freshly fetched previous manifest. This retains the fetched history; it does not modify the release's immutable manifest snapshot. If a newer release has already reached the branch, the generator refuses to publish this older version: stop and review the current feed rather than bypassing its version-order guard.

   ```bash
   python tools/generate_manifest.py --archive "recovery/$ARCHIVE_NAME" --tag "$TAG" --previous recovery/previous-manifest.json --output recovery/candidate-manifest.json --changelog "Release $TAG"
   ```

   Review the entire candidate: all existing versions and source URLs must remain, the intended version must reference its already-verified public ZIP, and its MD5 must match. If the generator refuses same-version changed bytes, stop rather than bypassing the check.

4. Create a separate worktree from `REPOSITORY_BASE`, or an orphan branch **only if the successful remote check proved absence**. Copy only the reviewed candidate to its `manifest.json`, inspect the diff, and commit that one file. Push `HEAD:refs/heads/repository` normally, **never with force**. If another publisher changed the branch, fetch its new state and regenerate/review the candidate instead of resetting it; stop if a newer version makes this recovery obsolete. If there is no actual change, do not create an empty commit just to retry a check.
5. Read the remote ref back and confirm it equals the intended worktree commit. Then verify the stable public URL:

   ```bash
   python tools/verify_repository.py --manifest-url https://raw.githubusercontent.com/everyonesoftware/collection-image-generator-jellyfin-plugin/repository/manifest.json --version "$ASSEMBLY_VERSION"
   ```

   Retry transient raw-content caching finitely (the workflow uses ten attempts, 30 seconds apart). Persistent failures require investigation, not an unbounded retry or history rewrite. Only after this passes should users be told the new catalog version is available.

### Stable URL verification failed after a successful push

First check that the remote commit and manifest are the intended ones. Retry anonymous verification after the raw-content cache has refreshed. Preserve the branch and assets while diagnosing failures; do not force-push an old manifest or delete release history. If a release is genuinely defective, document the problem and publish a corrected new version through the normal two-OS gate.
