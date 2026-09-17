using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UglyToad.PdfPig;

namespace Print2Md.Core;

/// <summary>Extracts PDF text, with optional rendered-page OCR for pages without text.</summary>
public sealed class PdfToMarkdownConverter
{
    public async Task<ConversionResult> ConvertAsync(Stream input, ConversionOptions options,
        CancellationToken token, IPageTextRecognizer? recognizer = null)
    {
        if (input == null) throw new ArgumentNullException(nameof(input));
        if (options == null) throw new ArgumentNullException(nameof(options));
        OxpsToMarkdownConverter.ValidateOptions(options);
        token.ThrowIfCancellationRequested();
        var document = new XpsDocumentModel();
        var warnings = new List<ConversionWarning>();
        var imageCount = 0;
        using (var buffer = new MemoryStream())
        {
            await input.CopyToAsync(buffer, 81920, token).ConfigureAwait(false);
            buffer.Position = 0;
            try
            {
                using (var pdf = PdfDocument.Open(buffer))
                {
                    foreach (var source in pdf.GetPages())
                    {
                        token.ThrowIfCancellationRequested();
                        var page = new XpsPageModel
                        {
                            Number = source.Number, Width = source.Width, Height = source.Height,
                            GlyphRunCount = source.Letters.Count,
                        };
                        foreach (var word in source.GetWords())
                        {
                            if (string.IsNullOrWhiteSpace(word.Text)) continue;
                            var letter = word.Letters[0];
                            var font = letter.FontName ?? string.Empty;
                            page.TextRuns.Add(new TextRunModel
                            {
                                Text = word.Text, X = word.BoundingBox.Left,
                                Y = source.Height - letter.StartBaseLine.Y,
                                Width = Math.Max(1, word.BoundingBox.Width),
                                FontSize = Math.Max(1, letter.PointSize),
                                Bold = font.IndexOf("Bold", StringComparison.OrdinalIgnoreCase) >= 0,
                                Italic = font.IndexOf("Italic", StringComparison.OrdinalIgnoreCase) >= 0 || font.IndexOf("Oblique", StringComparison.OrdinalIgnoreCase) >= 0,
                            });
                        }
                        imageCount += source.NumberOfImages;
                        foreach (var image in source.GetImages())
                        {
                            // Keep placement for the omission marker without decoding or saving image bytes.
                            page.Images.Add(new ImageModel
                            {
                                X = image.BoundingBox.Left, Y = source.Height - image.BoundingBox.Top,
                                Width = image.BoundingBox.Width, Height = image.BoundingBox.Height, Omitted = true,
                            });
                            warnings.Add(new ConversionWarning("image-omitted", "An image was omitted from the Markdown.", page.Number));
                        }
                        document.Pages.Add(page);
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                throw new ConversionException(ConversionFailure.InvalidPdf, "The PDF could not be read; it may be damaged or encrypted.", exception);
            }
        }
        if (document.Pages.Count == 0)
            throw new ConversionException(ConversionFailure.NoPages, "The PDF contains no pages.");
        if (recognizer != null)
            await PageTextRecovery.RecoverAsync(document, recognizer, warnings, token).ConfigureAwait(false);
        else if (document.Pages.All(page => page.TextRuns.Count == 0))
            throw new ConversionException(ConversionFailure.NoExtractableText, "The PDF has no extractable text and requires OCR.");
        var markdown = new LayoutAnalyzer(options).Render(document, warnings);
        if (string.IsNullOrWhiteSpace(markdown))
            throw new ConversionException(ConversionFailure.NoExtractableText, "No readable text remained after layout processing.");
        return new ConversionResult(markdown, new List<AssetReference>().AsReadOnly(), warnings.AsReadOnly(),
            document.Pages.Count, document.Pages.Sum(page => page.GlyphRunCount), 0, imageCount);
    }
}
