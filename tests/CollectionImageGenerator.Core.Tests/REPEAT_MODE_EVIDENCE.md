# Repeat-mode native rendering evidence

Executed on Windows with .NET SDK 10.0.403 and SkiaSharp 3.119.4.
No live Jellyfin/library data was used.

## Vertical RED → GREEN slices

Commands used this SDK executable (Git Bash):

```bash
DOTNET='C:/Users/dansc/AppData/Local/hermes/cache/scratch/dotnet10/dotnet.exe'
```

For each filter below, RED ran before its production change:

```bash
"$DOTNET" test tests/CollectionImageGenerator.Core.Tests -c Release --filter FullyQualifiedName~PublicRepeatModeHasStableValuesAndDefaultsToSimpleRepeat
# RED: 1 failed, missing public enum (Assert.NotNull).
# Added enum and default option. Same command GREEN: 1 passed.

"$DOTNET" test tests/CollectionImageGenerator.Core.Tests -c Release --filter FullyQualifiedName~TwoUsablePostersAreCheckered
# RED: 2 failed, lower-left red instead of lime.
# Added two-decoded-source ABBA mapping.
"$DOTNET" test tests/CollectionImageGenerator.Core.Tests -c Release --filter FullyQualifiedName~RepeatModeTests
# GREEN: 3 passed.

"$DOTNET" test tests/CollectionImageGenerator.Core.Tests -c Release --filter FullyQualifiedName~NoRepeatLeavesUnusedQuadrantsTransparent
# RED: 3 failed, 1 passed; repeated red where alpha-zero expected.
# Added explicit transparent clear and limited NoRepeat quadrant count.
"$DOTNET" test tests/CollectionImageGenerator.Core.Tests -c Release --filter FullyQualifiedName~RepeatModeTests
# GREEN: 7 passed.

"$DOTNET" test tests/CollectionImageGenerator.Core.Tests -c Release --filter FullyQualifiedName~InvalidRepeatModeIsRejectedByRendererAndFingerprint
# RED: 3 failed; no exception for -1, 3, Int32.MaxValue.
# Added shared enum validation.
"$DOTNET" test tests/CollectionImageGenerator.Core.Tests -c Release --filter FullyQualifiedName~RepeatModeTests
# GREEN: 10 passed.

"$DOTNET" test tests/CollectionImageGenerator.Core.Tests -c Release --filter FullyQualifiedName~FingerprintsChangeAcrossAllRepeatModesEvenWithFourSources
# RED: 1 failed; only 1 distinct fingerprint, expected 3.
# Added repeat-mode fingerprint field and bumped renderer marker to v2.
"$DOTNET" test tests/CollectionImageGenerator.Core.Tests -c Release --filter FullyQualifiedName~RepeatModeTests
# GREEN: 11 passed.
```

Additional characterization covers all 3 modes × 4 decoded source counts ×
2 widths, every decoded pixel including alpha/odd-size seams, four
unusable input paths before valid sources, default byte-for-byte compatibility,
unchanged all-unusable errors, existing cover cropping, and independent text
bands painting otherwise empty quadrants. Existing tests were not modified.

## Final verification

```bash
REPEAT_MODE_SAMPLE_PATH='C:/Users/dansc/Code/collection-image-generator-jellyfin-plugin/docs/sample-repeat-modes.png' "$DOTNET" test tests/CollectionImageGenerator.Core.Tests -c Release --filter FullyQualifiedName~RepeatMode
# 40 passed; actual comparison PNG generated and visually inspected.
"$DOTNET" build src/CollectionImageGenerator.Core -c Release
# Succeeded: 0 warnings, 0 errors.
"$DOTNET" test tests/CollectionImageGenerator.Core.Tests -c Release --logger 'trx;LogFileName=repeat-mode-release.trx'
# 92 passed, 0 failed/skipped.
git diff --check
# Passed.
```

Sample: `docs/sample-repeat-modes.png`, 960×1100, synthetic A/B/C posters only.
Rows compare two and three sources; columns compare all three modes. Checkerboard
is added behind the separately decoded NoRepeat PNGs to visualize transparency;
the renderer output's alpha is independently asserted. The sample generator uses
the embedded licensed font and exports only when `REPEAT_MODE_SAMPLE_PATH` is set.
Native rendering was verified on Windows only, not Linux or live Jellyfin.
