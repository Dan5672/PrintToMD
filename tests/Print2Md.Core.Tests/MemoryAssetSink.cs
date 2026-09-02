using Print2Md.Core;

namespace Print2Md.Core.Tests;

internal sealed class MemoryAssetSink : IAssetSink
{
    public Dictionary<string, byte[]> Assets { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<AssetReference> WriteAsync(AssetContent asset, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var extension = asset.ContentType switch
        {
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            _ => ".png",
        };
        var name = asset.ContentHash[..24] + extension;
        Assets[name] = asset.Bytes;
        return Task.FromResult(new AssetReference("document.assets/" + name, asset.ContentType));
    }
}

