using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Print2Md.Core;

/// <summary>Converts a seekable or forward-only OXPS print stream to semantic Markdown.</summary>
public sealed class OxpsToMarkdownConverter
{
    public async Task<ConversionResult> ConvertAsync(
        Stream oxps,
        ConversionOptions options,
        IAssetSink assetSink,
        CancellationToken cancellationToken)
    {
        if (oxps == null) throw new ArgumentNullException(nameof(oxps));
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (assetSink == null) throw new ArgumentNullException(nameof(assetSink));

        ValidateOptions(options);
        var warnings = new List<ConversionWarning>();
        var assets = new List<AssetReference>();
        var reader = new OxpsPackageReader(warnings);
        var document = await reader.ReadAsync(oxps, cancellationToken).ConfigureAwait(false);

        foreach (var page in document.Pages)
        {
            foreach (var image in page.Images)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var reference = await assetSink.WriteAsync(
                        new AssetContent(image.ContentHash, image.ContentType, image.Bytes, page.Number),
                        cancellationToken).ConfigureAwait(false);
                    image.Reference = reference;
                    assets.Add(reference);
                }
                catch (Exception exception) when (!(exception is OperationCanceledException))
                {
                    warnings.Add(new ConversionWarning("image-write-failed", $"An image could not be written ({exception.GetType().Name}).", page.Number));
                }
            }
        }

        var markdown = new LayoutAnalyzer(options).Render(document, warnings);
        return new ConversionResult(markdown, assets.AsReadOnly(), warnings.AsReadOnly(), document.Pages.Count);
    }

    private static void ValidateOptions(ConversionOptions options)
    {
        if (options.MarginFraction < 0 || options.MarginFraction > 0.25)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MarginFraction must be between 0 and 0.25.");
        }

        if (options.MinimumRepeatPages < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumRepeatPages must be at least 2.");
        }

        if (options.RepeatDocumentFraction <= 0 || options.RepeatDocumentFraction > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "RepeatDocumentFraction must be greater than 0 and no more than 1.");
        }
    }
}
