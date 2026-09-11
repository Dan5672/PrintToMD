#nullable enable

using Print2Md.Core;
using System.Threading;
using System.Threading.Tasks;

namespace Print2Md.Tasks;

/// <summary>
/// Declines every image. The print system grants this background task access to the
/// Markdown file the user chose and to nothing else, so a sibling asset folder cannot
/// be created next to it. Images are omitted and reported in the Markdown instead.
/// </summary>
internal sealed class OmittedAssetSink : IAssetSink
{
    public Task<AssetReference?> WriteAsync(AssetContent asset, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<AssetReference?>(null);
    }
}
