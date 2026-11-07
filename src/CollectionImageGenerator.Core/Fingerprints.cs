namespace CollectionImageGenerator.Core;
public static class Fingerprints
{
    public static string Compute(IEnumerable<(string Id, string ContentHash)> sources, SelectionMode mode, string collectionName, CollageOptions options)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _ = OptionValidation.Text(collectionName, options);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        void Add(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            var bytes = System.Text.Encoding.UTF8.GetBytes(value);
            Span<byte> length = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }
        void Number(int value) => Add(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        // Bump this version whenever renderer/font behavior changes to invalidate prior generated artwork.
        Add("CollectionImageGenerator.Core/v1/NotoSans-Regular");
        Number((int)mode);
        Add(collectionName);
        Add(options.EnableText ? "1" : "0");
        Add(options.TextTemplate);
        Number((int)options.TextPosition);
        Number(options.Width);
        Number(options.Height);
        Number(options.FontSize);
        foreach (var source in sources)
        {
            Add(source.Id);
            Add(source.ContentHash);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
