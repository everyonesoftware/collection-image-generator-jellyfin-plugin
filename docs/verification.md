# Verification notes

## Local Windows execution

- .NET SDK 10.0.403; build targets .NET 10 and Jellyfin packages 12.4.0.
- Full Release solution build completed with zero warnings/errors.
- Core test suite: 104 passed, zero failures/skips.
- Plugin lifecycle/configuration/ownership test suite: 130 passed, zero failures/skips.
- Python packaging/distribution suite: 13 passed.
- Node settings-script suite: 16 passed, exercising the actual embedded JavaScript against mock API/DOM boundaries.
- Native SkiaSharp 3.119.4 renderer executed; synthetic sample PNGs inspected.
- Packaged ZIP loaded through a standalone host using Jellyfin 12.4's exact `AssemblyDependencyResolver`/`AssemblyLoadContext` algorithm. Verified discoverable scheduled/post-scan and hosted-service exports and isolated resolution of the packaged core assembly, with no plugin-project reference in the probe host.
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

## Repeat layouts (0.1.2)

The native renderer's pixel/alpha tests cover all three modes with one through four usable posters, including odd output dimensions and invalid/missing sources. Two-source checkerboard is explicitly checked as A B / B A. Do-not-repeat leaves unpainted quadrants transparent; text remains an independent overlay. Fingerprint tests establish setting-driven invalidation.

Plugin tests verify legacy XML defaults, XML persistence and conversion to render options, mode changes updating plugin-owned images once, unchanged runs skipping work, and custom artwork remaining protected. Chromium exercised all three dropdown choices through Save and reload, numeric enum loading, and the legacy default without an implicit save. Desktop and 390-pixel fixture views were inspected without horizontal overflow; these use fake API boundaries and do not establish live Jellyfin execution. `docs/sample-repeat-modes.png` is actual rendered output from labeled synthetic poster fixtures and was visually inspected.

## Collection-content updates (0.1.3)

Hosted-service tests exercise subscription to collection creation/addition/removal, duplicate-start protection, per-ID quiet-period reset and deduplication, affected-ID-only processing, events received during an active run, configuration checks after the shared processing gate, worker error recovery, and shutdown/unsubscription. These are local boundary tests, not live-server operations. Throwing and deliberately blocked configuration getters prove all three event callbacks return without propagating configuration failures or waiting for configuration I/O; subsequent healthy events recover without replaying discarded work.

Empty-collection tests cover trusted path/hash ownership, custom artwork even with overwrite enabled, changed or uncertain ownership, nested members, missing/corrupt member posters, intervening artwork or membership changes, failed/unverifiable deletion, durable evidence removal, no-op repetition, and regeneration after readdition. The real adapter calls Jellyfin's image-deletion API; a cancellation-during-delete regression verifies completed deletion can retain its durable evidence and safely regenerate on a later run. The host operation is noncancellable once entered, and the final-check-to-mutation race remains documented.

The actual embedded checkbox was exercised in Chromium against fake API boundaries, including default-on behavior, saving an opt-out, and reload. Desktop and 390-pixel fixture views were inspected without horizontal overflow or captured script errors. Node tests also verify older configurations default on and an explicit opt-out persists. The packaged-loader probe checks the new hosted-service export; registration and lifecycle tests separately establish its wiring. None of these checks establishes installation in a live Jellyfin instance.

## Runtime ordering (0.1.4)

Core tests preserve existing enum values and exercise shortest/longest ordering, invalid runtimes last in both directions, deterministic ties and duplicate representatives, the four-item limit, and odd/even positive-only medians including overflow boundaries. Adapter tests verify positive movie runtime without episode queries and TV episode queries scoped by stored logical series identity or a safe ancestor-ID fallback. Read-only hydration, specials/virtual/missing/invalid episode filtering, duplicate IDs, and no aggregate-series fallback are covered without invoking host-global derived metadata getters.

Processor tests render real synthetic poster fixtures and check quadrant colors in both runtime directions, regeneration and unchanged-output skipping, custom-artwork protection, no runtime reads for older ordering modes or unusable posters, and per-run caching that refreshes on later full or targeted runs. No episode-added listener or average/median setting was introduced.

The actual embedded settings HTML was exercised in Chromium with fake API boundaries: numeric values 3/4 loaded the new options, both named choices survived Save and reload, unrelated future configuration remained intact, and no generation request occurred. Loading calls balanced with no captured script errors. Desktop and 390-pixel fixture views had no horizontal overflow; this does not establish live Jellyfin styling or execution. Node tests cover numeric/named values for all five ordering modes, and the compiled embedded page asserts both new option values.

## Independent review

The core review found a reproducible word-boundary wrapping issue at minimum size. A separate fix agent added failing regression tests, applied the fix, and ran the full suite. A subsequent independent scoped review passed. Integration review found a TV-series exclusion bug; its fix and the additional corrupt-poster-before-selection regression passed independent re-review. Final scoped review reported no security or logic findings and reran both .NET suites successfully.

For collection-content updates, separate reviews covered listener lifecycle and empty-artwork deletion. The listener review found configuration access could throw or block Jellyfin's collection mutation callbacks. A separate fix agent reproduced nine failing cases, moved configuration access into the managed worker, and restored the full suite. Final independent re-review reported no security or logic findings and reran all 111 plugin tests and 15 settings-script tests successfully.

The runtime-ordering review covered the full implementation, tests, embedded page, and verification notes with no findings. It checked the matching host source for ancestor-query behavior and independently reran 104 core tests, 130 plugin tests, and all 16 settings-script tests successfully.

## Cross-platform test isolation

The test projects explicitly supply their own Windows and Linux SkiaSharp native assets; the distributable ZIP still relies on Jellyfin's host-provided native libraries. Word-wrap regression fixtures derive their widths from measured font advances rather than fixed pixel thresholds, while retaining exact expected lines and break-boundary assertions. This avoids platform-specific font metric assumptions without relaxing the regression coverage.

## Not yet established by these checks

- Docker Desktop's Linux daemon was unreachable during initial discovery. No real Jellyfin container or user library has been accessed or modified.
- This file's local results do not establish Linux runtime support; the GitHub Actions Linux/Windows matrix is a separate check.
- After installing, verify first with a disposable collection and a backup of `/config`.
- Plugin-level locking cannot make saves atomic against an unrelated provider or simultaneous administrator artwork upload. See README.
