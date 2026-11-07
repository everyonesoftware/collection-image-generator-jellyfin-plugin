# Standalone collage core

Targets .NET 10; references SkiaSharp 3.119.4, not Jellyfin. The host supplies native Skia. Tests explicitly include the matching Windows and Linux.NoDependencies packages.

## Public API

Namespace: `CollectionImageGenerator.Core`.

- `enum SelectionMode { ReleaseDate, Alphabetical, StableRandom }`
- `enum TextPosition { Top, Middle, Bottom }`
- `record PosterCandidate(string Id, string Name, DateTime? PremiereDate, string Path)`
- `CollageOptions`: mutable `EnableText` (false), `TextTemplate` (`"{name}"`), `TextPosition` (Bottom), `Width` (1000), `Height` (1500), `FontSize` (80) properties.
- `PosterSelector.Select(IEnumerable<PosterCandidate> candidates, SelectionMode mode, string collectionId) : IReadOnlyList<PosterCandidate>`
- `CollageRenderer.Render(IReadOnlyList<string> posterPaths, string collectionName, CollageOptions options) : byte[]`
- `ArtworkPolicy.CanReplace(string? currentHash, string? ownedHash, bool overwriteExisting = false) : bool`
- `Fingerprints.Compute(IEnumerable<(string Id, string ContentHash)> sources, SelectionMode mode, string collectionName, CollageOptions options) : string`

No signature deviations. Fingerprints are uppercase SHA256 hex with ordered, UTF-8 length-framed fields and a renderer/font version marker; source order, IDs, content hashes, mode, name and all options contribute. Bump the marker when changing renderer/font behavior.

Selection yields at most four ordinal-distinct IDs. Dates ascend with nulls strictly last; name ties use OrdinalIgnoreCase then ordinal ID. Alphabetical uses OrdinalIgnoreCase then ordinal ID. Random sorts lexicographically by SHA256 UTF-8 `collectionId + "\0" + candidate.Id`, then ordinal ID. Additional metadata/path ties make conflicting duplicate records canonical across input reorder.

Rendering reads source files and an assembly-embedded font; it does not write files, modify posters, or access a server. It uses the first four usable images, cycles available images to fill four quadrants, cover-crops from each image center and covers odd output edges. Unreadable, corrupt, incomplete and oversized sources are skipped. Zero usable sources throws `InvalidOperationException` with an explicit message.

Text replaces literal `{name}`, centers wrapped lines over an alpha-175 black band at Top/Middle/Bottom and uses embedded Noto Sans exclusively. Font search is bounded; long words break at grapheme boundaries. Text shrinks to eight pixels and ellipsizes only if it cannot fit at that minimum. The band uses at most 45% of the output height. This is simple glyph rendering, not a complex-script shaping engine, and there is no system-font fallback for unsupported glyphs.

Ownership comparison ignores hex case; null/empty current hash is missing artwork. A nonempty existing hash is replaceable only if it matches a nonempty recorded owned hash or explicit overwrite is requested. Callers must not represent an existing but unreadable image as a missing hash.

## Bounds (validated before output allocation)

- Output width/height: 64..4096 each; total at most 8,388,608 pixels.
- Font size: 8..512, even when text is disabled.
- Name/template: 4096 UTF-16 code units each; expanded text: 8192.
- Source path list: at most 256; only four decoded images retained.
- Source compressed length: at most 32 MiB; header dimensions: 1..8192 each and at most 8,388,608 pixels. Headers are inspected before allocating decoded pixels. Oversized sources are skipped, not silently downsampled.
- Text layout: binary font search over the validated range; at most 64 lines per trial.

Bad options throw `ArgumentNullException` or `ArgumentOutOfRangeException` rather than allocating unbounded images/layouts.

## Font provenance

Unmodified `NotoSans-Regular.ttf` from the Noto Project, embedded as `CollectionImageGenerator.Core.Fonts.NotoSans-Regular.ttf`. Copyright and SIL Open Font License 1.1 are preserved in `Fonts/OFL.txt`, copied to build output and included when packing this project.

Source: https://github.com/notofonts/noto-fonts/blob/main/hinted/ttf/NotoSans/NotoSans-Regular.ttf

License source: https://github.com/notofonts/noto-fonts/blob/main/LICENSE

Font SHA256: `b85c38ecea8a7cfb39c24e395a4007474fa5a4fc864f6ee33309eb4948d232d5`.
