using Print2Md.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Print2Md.Tasks;

internal sealed class WindowsPageTextRecognizer : IPageTextRecognizer, IDisposable
{
    private readonly Func<Task<IRandomAccessStream>> createPdf;
    private readonly Func<string, Task> progress;
    private IRandomAccessStream pdfStream;
    private PdfDocument pdf;
    private OcrEngine engine;

    internal WindowsPageTextRecognizer(Func<Task<IRandomAccessStream>> createPdf, Func<string, Task> progress)
    {
        this.createPdf = createPdf;
        this.progress = progress;
    }

    public async Task<IReadOnlyList<RecognizedTextLine>> RecognizeAsync(int pageNumber, double width, double height, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (engine == null)
        {
            engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null)
            {
                var language = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault();
                if (language != null) engine = OcrEngine.TryCreateFromLanguage(language);
            }
            if (engine == null)
                throw new ConversionException(ConversionFailure.OcrUnavailable, "Install a Windows OCR language to recognize this document.");
        }
        if (pdf == null)
        {
            await progress("prepare-ocr-pdf");
            pdfStream = await createPdf();
            pdfStream.Seek(0);
            pdf = await PdfDocument.LoadFromStreamAsync(pdfStream).AsTask(token);
        }
        await progress("ocr-page-" + pageNumber);
        using (var page = pdf.GetPage((uint)(pageNumber - 1)))
        using (var rendered = new InMemoryRandomAccessStream())
        {
            var maximum = Math.Min(4096, OcrEngine.MaxImageDimension);
            var scale = Math.Min(200.0 / 96, maximum / Math.Max(page.Size.Width, page.Size.Height));
            var renderOptions = new PdfPageRenderOptions
            {
                DestinationWidth = (uint)Math.Max(1, Math.Round(page.Size.Width * scale)),
                DestinationHeight = (uint)Math.Max(1, Math.Round(page.Size.Height * scale)),
            };
            await page.RenderToStreamAsync(rendered, renderOptions).AsTask(token);
            rendered.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(rendered).AsTask(token);
            using (var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied).AsTask(token))
            {
                var result = await engine.RecognizeAsync(bitmap).AsTask(token);
                var lines = new List<RecognizedTextLine>();
                foreach (var line in result.Lines.Where(line => line.Words.Count > 0))
                {
                    var left = line.Words.Min(word => word.BoundingRect.Left);
                    var top = line.Words.Min(word => word.BoundingRect.Top);
                    var right = line.Words.Max(word => word.BoundingRect.Right);
                    var bottom = line.Words.Max(word => word.BoundingRect.Bottom);
                    lines.Add(new RecognizedTextLine(line.Text, left * width / bitmap.PixelWidth,
                        top * height / bitmap.PixelHeight, (right - left) * width / bitmap.PixelWidth,
                        (bottom - top) * height / bitmap.PixelHeight));
                }
                await progress("ocr-result page=" + pageNumber + " lines=" + lines.Count);
                return lines;
            }
        }
    }

    public void Dispose()
    {
        pdf = null;
        pdfStream?.Dispose();
        pdfStream = null;
    }
}
