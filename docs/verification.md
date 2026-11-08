# Verification notes

## Local Windows execution

- .NET SDK 10.0.403; build targets .NET 10 and Jellyfin packages 12.4.0.
- Full Release solution build completed with zero warnings/errors.
- Core test suite: 52 passed, zero failures/skips.
- Plugin lifecycle/configuration/ownership test suite: 51 passed, zero failures/skips.
- Python packaging/distribution suite: 13 passed.
- Node settings-script suite: 12 passed, exercising the actual embedded JavaScript against mock API/DOM boundaries.
- Native SkiaSharp 3.119.4 renderer executed; synthetic sample PNGs inspected.
- Packaged ZIP loaded through a standalone host using Jellyfin 12.4's exact `AssemblyDependencyResolver`/`AssemblyLoadContext` algorithm. Verified discoverable scheduled/post-scan exports and isolated resolution of the packaged core assembly, with no plugin-project reference in the probe host.
- NuGet vulnerability audit reported no vulnerable dependencies for core, plugin, and loader-probe projects using the configured NuGet source.

## Settings browser checks

Exercised the **actual embedded settings HTML** in Chromium using a deliberately mocked `ApiClient` and `Dashboard`. This is a functional fixture check, not a live Jellyfin dashboard or a claim of matching host CSS.

Verified:

- Numeric enum responses load as release-date/bottom defaults.
- Text and overwrite controls default off.
- Stable-random and middle-position selections, optional title text, and numeric settings reach the save payload with correct types.
- Reload displays saved settings.
- An unrelated future config field survives the read/modify/write round trip.
- Literal `<script>` text stays an input value, not markup.
- Oversized output dimensions do not save and display a validation message.
- Save failure displays an error and restores the Save button.
- Load failure disables Save to prevent accidental default overwrites.
- No captured JavaScript errors or unhandled promise rejections; loading calls balance.

## Settings-page run-once action (0.1.1)

Chromium exercised the actual embedded HTML with the official API client's task-list/start method bodies and deliberately fake server boundaries. The real client methods produced `GET ScheduledTasks` and `POST ScheduledTasks/Running/collection-images` for the fixture task ID. This was not a live-server execution.

Verified successful request feedback without a configuration save, an already-running task without a second start, start-request error recovery, blocked duplicate clicks and Save during an in-flight request, and balanced loading indicators with no captured script errors. Desktop and 390-pixel-wide fixture views were inspected; neither had horizontal overflow. Jellyfin host styling was not included in this fixture. Node tests additionally cover saved-disabled configuration, discovery/state failures, numeric enum values, and concurrency in both directions. The compiled embedded page and actual assembly version are checked by the .NET lifecycle suite.

## Independent review

The core review found a reproducible word-boundary wrapping issue at minimum size. A separate fix agent added failing regression tests, applied the fix, and ran the full suite. A subsequent independent scoped review passed. Integration review found a TV-series exclusion bug; its fix and the additional corrupt-poster-before-selection regression passed independent re-review. Final scoped review reported no security or logic findings and reran both .NET suites successfully.

## Cross-platform test isolation

The test projects explicitly supply their own Windows and Linux SkiaSharp native assets; the distributable ZIP still relies on Jellyfin's host-provided native libraries. Word-wrap regression fixtures derive their widths from measured font advances rather than fixed pixel thresholds, while retaining exact expected lines and break-boundary assertions. This avoids platform-specific font metric assumptions without relaxing the regression coverage.

## Not yet established by these checks

- Docker Desktop's Linux daemon was unreachable during initial discovery. No real Jellyfin container or user library has been accessed or modified.
- This file's local results do not establish Linux runtime support; the GitHub Actions Linux/Windows matrix is a separate check.
- After installing, verify first with a disposable collection and a backup of `/config`.
- Plugin-level locking cannot make saves atomic against an unrelated provider or simultaneous administrator artwork upload. See README.
