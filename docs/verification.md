# Verification notes

## Local Windows execution

- .NET SDK 10.0.403; build targets .NET 10 and Jellyfin packages 12.4.0.
- Full Release solution build completed with zero warnings/errors.
- Core test suite: 52 passed, zero failures/skips.
- Plugin lifecycle/configuration/ownership test suite: 51 passed, zero failures/skips.
- Python packaging suite: 2 passed.
- Node settings-script suite: 4 passed, exercising the actual embedded JavaScript against mock API/DOM boundaries.
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

## Independent review

The core review found a reproducible word-boundary wrapping issue at minimum size. A separate fix agent added failing regression tests, applied the fix, and ran the full suite. A subsequent independent scoped review passed. Integration review found a TV-series exclusion bug; its fix and the additional corrupt-poster-before-selection regression passed independent re-review. Final scoped review reported no security or logic findings and reran both .NET suites successfully.

## Not yet established by these checks

- Docker Desktop's Linux daemon was unreachable during initial discovery. No real Jellyfin container or user library has been accessed or modified.
- This file's local results do not establish Linux runtime support; the GitHub Actions Linux/Windows matrix is a separate check.
- After installing, verify first with a disposable collection and a backup of `/config`.
- Plugin-level locking cannot make saves atomic against an unrelated provider or simultaneous administrator artwork upload. See README.
