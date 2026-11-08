namespace CollectionImageGenerator.Core;
public static class CollageRenderer
{
    public static byte[] Render(IReadOnlyList<string> posterPaths, string collectionName, CollageOptions options)
    {
        ArgumentNullException.ThrowIfNull(posterPaths);
        var text = OptionValidation.Text(collectionName, options);
        if (posterPaths.Count > 256) throw new ArgumentOutOfRangeException(nameof(posterPaths), "At most 256 source paths can be inspected.");
        var images = new List<SkiaSharp.SKBitmap>();
        try
        {
            foreach (var path in posterPaths)
            {
                var image = ReadPoster(path);
                if (image is not null) images.Add(image);
                if (images.Count == 4) break;
            }
            if (images.Count == 0) throw new InvalidOperationException("No usable poster images were found.");
            using var result = new SkiaSharp.SKBitmap(options.Width, options.Height);
            using var canvas = new SkiaSharp.SKCanvas(result);
            canvas.Clear(SkiaSharp.SKColors.Transparent);
            var quadrantCount = options.RepeatMode == RepeatMode.NoRepeat ? images.Count : 4;
            for (var i = 0; i < quadrantCount; i++)
            {
                var sourceIndex = options.RepeatMode == RepeatMode.RepeatCheckered && images.Count == 2
                    ? (i == 0 || i == 3 ? 0 : 1)
                    : i % images.Count;
                var image = images[sourceIndex];
                var left = i % 2 == 0 ? 0 : options.Width / 2;
                var top = i / 2 == 0 ? 0 : options.Height / 2;
                var right = i % 2 == 0 ? options.Width / 2 : options.Width;
                var bottom = i / 2 == 0 ? options.Height / 2 : options.Height;
                var dest = new SkiaSharp.SKRect(left, top, right, bottom);
                var scale = Math.Max(dest.Width / image.Width, dest.Height / image.Height);
                var cropWidth = dest.Width / scale;
                var cropHeight = dest.Height / scale;
                var source = SkiaSharp.SKRect.Create((image.Width - cropWidth) / 2, (image.Height - cropHeight) / 2, cropWidth, cropHeight);
                canvas.DrawBitmap(image, source, dest);
            }
            if (options.EnableText) TextOverlay.Draw(canvas, text, options);
            using var png = result.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            return png.ToArray();
        }
        finally
        {
            foreach (var image in images) image.Dispose();
        }
    }

    // Validate with the same bounded decoder used by Render before selecting four.
    public static bool IsUsablePoster(string path)
    {
        using var image = ReadPoster(path);
        return image is not null;
    }

    private static SkiaSharp.SKBitmap? ReadPoster(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || path.Contains('\0')) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length is <= 0 or > 33_554_432) return null;
            using var codec = SkiaSharp.SKCodec.Create(stream);
            if (codec is null) return null;
            var info = codec.Info;
            if (info.Width is <= 0 or > 8192 || info.Height is <= 0 or > 8192
                || (long)info.Width * info.Height > 8_388_608) return null;
            var image = new SkiaSharp.SKBitmap(info.Width, info.Height, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Premul);
            var transferred = false;
            try
            {
                if (codec.GetPixels(image.Info, image.GetPixels()) != SkiaSharp.SKCodecResult.Success) return null;
                transferred = true;
                return image;
            }
            finally
            {
                if (!transferred) image.Dispose();
            }
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
