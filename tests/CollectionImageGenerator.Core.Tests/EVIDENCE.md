# Core test evidence

Executed locally with the provided .NET SDK **10.0.403**, targeting **net10.0**, on native Windows. No Jellyfin instance or Docker daemon was accessed. Core and test-project files only, plus four explicitly fictional `docs/sample*.png` artifacts.

## Observed TDD cycles

Each row records a real focused RED run followed by implementation and GREEN. Full raw console logs are retained alongside this document as `red-*.log` / `green-*.log` (ignored if the root ignore policy excludes logs).

| Slice | Observed RED | Observed GREEN |
|---|---|---|
| Release ordering, ties, missing dates, distinct IDs, four-item limit | 1 NotImplemented failure (`red-selection.log`) | 1 passed |
| Alphabetical and SHA256 stable random | 2 assertion failures (`red-modes.log`) | 3 passed overall |
| Four quadrant rendering and ownership preservation | 7 NotImplemented failures (`red-basic-render-policy.log`) | 10 passed overall |
| Fewer than four images, corrupt fallback, explicit empty error, cover crop and odd edges | 6 failures (`red-fallback-crop.log`) | 16 passed overall |
| Bounded dimensions/font/text/source length and source headers | 10 missing-exception failures (`red-bounds.log`) | 26 passed overall |
| Text wrapping, positions, long unbroken words, shrink/ellipsis and templates | 6 pixel/output assertion failures (`red-text.log`) | 6 focused passed |
| Ordered content fingerprints and extreme aspect-ratio text band | 3 failures (`red-fingerprints-wide.log`) | 35 passed overall |
| DateTime.MaxValue before missing dates; malformed source path fallback | 2 failures (`red-edgecases.log`) | 37 passed overall |

Initial harness setup also exposed a missing embedded font file and then absent public model symbols; these setup compiler errors were resolved before the meaningful NotImplemented RED. An xUnit analyzer error in a newly added assertion was corrected before the fingerprints/aspect-ratio RED. These are not presented as behavioral REDs.

The aspect-ratio regression caught padding derived only from width causing a full-height band at 4096x64. Padding is now capped by the allowed band height. The date regression caught a DateTime.MaxValue sentinel placing missing dates before a real maximum date; missingness is now an explicit primary key.

## Final verification

Commands (use the provided dotnet.exe path locally or `dotnet` on CI):

```text
dotnet build src/CollectionImageGenerator.Core/CollectionImageGenerator.Core.csproj -c Release --no-restore --nologo
dotnet test tests/CollectionImageGenerator.Core.Tests/CollectionImageGenerator.Core.Tests.csproj -c Release --nologo --logger "trx;LogFileName=core-release.trx" --results-directory tests/CollectionImageGenerator.Core.Tests/TestResults
```

Final build output: **Build succeeded. 0 Warning(s), 0 Error(s).**

Final test output: **Passed! Failed: 0, Passed: 38, Skipped: 0, Total: 38.** TRX: `TestResults/core-release.trx`. Console: `final-release-test.log`, `final-release-build.log`.

Coverage includes every pixel of the default 1000x1500 four-color fixture; quadrant centers/seams/edges; 1/2/3-poster repetition; corrupt/missing/malformed sources; center cover crop; odd output dimensions; font and allocation guards; top/middle/bottom band placement and visible wrapped glyph bounds; 4096-character unbroken text at 64x64; extreme wide aspect ratio; conservative ownership; all fingerprint options/content/order and culture independence; conflicting duplicate selection stability; deterministic fictional sample generation.

## Demo artifacts

`DemoSamplesTests` generates invented geometric posters labeled **TEST / DEMO**, **FICTIONAL POSTER**, and **No real server data** using the same embedded font. Set `COLLAGE_SAMPLE_OUTPUT` to the repository `docs` directory to regenerate:

- `sample-collage-no-text.png`
- `sample-collage-text-top.png`
- `sample-collage-text-middle.png`
- `sample-collage-text-bottom.png`

Every image decodes to 1000x1500. Repeated renders are byte-identical within the tested native environment. The middle sample was visually inspected: four panels, centered two-line title, visible translucent band, no clipped title glyphs. No real posters, collections, server IDs or user library data were used.

Linux native assets are included in the test project for the parent's Ubuntu CI matrix; **Linux execution was not performed locally**. Cross-platform PNG byte equality is not asserted (native text rasterization may differ). Runtime rendering does not require an installed system font.
