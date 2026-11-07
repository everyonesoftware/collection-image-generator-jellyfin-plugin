namespace CollectionImageGenerator.Core;
public static class ArtworkPolicy
{
    public static bool CanReplace(string? currentHash, string? ownedHash, bool overwriteExisting = false)
        => overwriteExisting || string.IsNullOrEmpty(currentHash)
            || (!string.IsNullOrEmpty(ownedHash) && string.Equals(currentHash, ownedHash, StringComparison.OrdinalIgnoreCase));
}
