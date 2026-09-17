using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Print2Md.Core;

/// <summary>Recognizes rendered page text locally when print data contains no usable text.</summary>
public interface IPageTextRecognizer
{
    Task<IReadOnlyList<RecognizedTextLine>> RecognizeAsync(int pageNumber, double width, double height, CancellationToken cancellationToken);
}

public sealed class RecognizedTextLine
{
    public RecognizedTextLine(string text, double x, double y, double width, double height)
    {
        Text = text;
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }
    public string Text { get; }
    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }
}

internal static class PageTextRecovery
{
    public static async Task RecoverAsync(XpsDocumentModel document, IPageTextRecognizer recognizer,
        IList<ConversionWarning> warnings, CancellationToken token)
    {
        foreach (var page in document.Pages.Where(page => page.TextRuns.Count == 0))
        {
            token.ThrowIfCancellationRequested();
            page.OcrAttempted = true;
            var lines = await recognizer.RecognizeAsync(page.Number, page.Width, page.Height, token).ConfigureAwait(false);
            foreach (var line in lines.Where(line => !string.IsNullOrWhiteSpace(line.Text)))
            {
                page.TextRuns.Add(new TextRunModel
                {
                    Text = line.Text, X = line.X, Y = line.Y + line.Height,
                    Width = Math.Max(1, line.Width), FontSize = Math.Max(1, line.Height),
                });
            }
            warnings.Add(new ConversionWarning("ocr-used", "Local OCR was used; review the recovered text for recognition errors.", page.Number));
        }
        if (document.Pages.All(page => page.TextRuns.Count == 0))
            throw new ConversionException(ConversionFailure.NoExtractableText, "No readable text was found, including after local OCR.");
    }
}
