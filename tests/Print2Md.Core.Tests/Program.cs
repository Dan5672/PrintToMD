using Print2Md.Core;
using Print2Md.Core.Tests;

var tests = new (string Name, Func<Task> Run)[]
{
    ("semantic markdown", SemanticMarkdown),
    ("repeated margins", RepeatedMargins),
    ("column reading order", ColumnReadingOrder),
    ("images and image-only warning", ImagesAndWarnings),
    ("forward-only input", ForwardOnlyInput),
    ("malformed package", MalformedPackage),
    ("conversion failure codes", ConversionFailureCodes),
    ("interleaved print package", InterleavedPrintPackage),
    ("invalid interleaved pieces", InvalidInterleavedPieces),
    ("missing image warning", MissingImageWarning),
    ("declined images omitted", DeclinedImagesAreOmitted),
    ("content counts", ContentCounts),
    ("PDF text extraction", PdfTextExtraction),
    ("PDF OCR only for textless pages", PdfOcrFallback),
    ("OXPS textless page recovery", OxpsOcrFallback),
    ("unreadable jobs fail", UnreadableJobsFail),
    ("PDF invalid input and cancellation", PdfFailureAndCancellation),
    ("cancellation", Cancellation),
    ("option validation", OptionValidation),
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
    }
}

Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed");
return failures == 0 ? 0 : 1;

static async Task SemanticMarkdown()
{
    var fixture = new OxpsFixtureBuilder();
    fixture.AddPage()
        .Glyph("Example Document", 60, 70, 26, "/Resources/Fonts/Bold.odttf")
        .Glyph("A printed para-", 60, 120)
        .Glyph("graph becomes editable text.", 60, 136)
        .Glyph("• First item", 60, 175)
        .Glyph("2) Second item", 60, 195)
        .Glyph("Name", 60, 240, 12, "/Resources/Fonts/Bold.odttf")
        .Glyph("Value", 300, 240, 12, "/Resources/Fonts/Bold.odttf")
        .Glyph("Alpha", 60, 260)
        .Glyph("42", 300, 260)
        .Glyph("https://example.com", 60, 310)
        .Glyph("Example link", 60, 335, link: "https://example.com/docs")
        .Glyph("Use *stars* [carefully]", 60, 360);

    var result = await Convert(fixture);
    AssertEx.Contains("# Example Document", result.Markdown);
    AssertEx.Contains("A printed paragraph becomes editable text.", result.Markdown);
    AssertEx.Contains("- First item", result.Markdown);
    AssertEx.Contains("2. Second item", result.Markdown);
    AssertEx.Contains("| Name | Value |", result.Markdown);
    AssertEx.Contains("| Alpha | 42 |", result.Markdown);
    AssertEx.Contains("<https://example.com>", result.Markdown);
    AssertEx.Contains("[Example link](https://example.com/docs)", result.Markdown);
    AssertEx.Contains("Use \\*stars\\* \\[carefully\\]", result.Markdown);
}

static async Task RepeatedMargins()
{
    var fixture = new OxpsFixtureBuilder();
    for (var pageNumber = 1; pageNumber <= 3; pageNumber++)
    {
        fixture.AddPage(800, 800)
            .Glyph("Quarterly report", 60, 35)
            .Glyph("Body page " + pageNumber, 60, 120)
            .Glyph("Page " + pageNumber, 370, 770);
    }

    var result = await Convert(fixture);
    AssertEx.DoesNotContain("Quarterly report", result.Markdown);
    AssertEx.DoesNotContain("Page 1", result.Markdown);
    AssertEx.Contains("Body page 1", result.Markdown);
    AssertEx.Contains("Body page 3", result.Markdown);
}

static async Task ColumnReadingOrder()
{
    var fixture = new OxpsFixtureBuilder();
    var page = fixture.AddPage(800, 800);
    for (var line = 1; line <= 4; line++)
    {
        page.Glyph("Left " + line, 60, 100 + (line * 20));
        page.Glyph("Right " + line, 460, 100 + (line * 20));
    }

    var result = await Convert(fixture);
    var left4 = result.Markdown.IndexOf("Left 4", StringComparison.Ordinal);
    var right1 = result.Markdown.IndexOf("Right 1", StringComparison.Ordinal);
    AssertEx.True(left4 >= 0 && right1 > left4, "The left column should be emitted before the right column." + Environment.NewLine + result.Markdown);
}

static async Task ImagesAndWarnings()
{
    var png = System.Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
    var fixture = new OxpsFixtureBuilder();
    fixture.AddPngResource("Resources/Images/pixel.png", png);
    fixture.AddPage().Image("/Resources/Images/pixel.png", 0, 0, 800, 800);
    var sink = new MemoryAssetSink();
    using var stream = fixture.Build();
    var result = await new OxpsToMarkdownConverter().ConvertAsync(stream, ConversionOptions.Default, sink, CancellationToken.None);

    AssertEx.Equal(1, sink.Assets.Count);
    AssertEx.Contains("![Image from page 1](document.assets/", result.Markdown);
    AssertEx.Contains("OCR was not performed", result.Markdown);
    AssertEx.True(result.Warnings.Any(warning => warning.Code == "ocr-not-performed"), "Expected an OCR warning.");
}

static MemoryStream PdfFixture(bool includeText, bool addBlankPage = false)
{
    var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
    var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
    var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
    if (includeText)
    {
        page.AddText("PDF conversion works", 24, new UglyToad.PdfPig.Core.PdfPoint(50, 760), font);
        page.AddText("This is selectable PDF text.", 12, new UglyToad.PdfPig.Core.PdfPoint(50, 700), font);
        page.AddText("Another paragraph with *literal* stars.", 12, new UglyToad.PdfPig.Core.PdfPoint(50, 660), font);
    }
    if (addBlankPage) builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
    return new MemoryStream(builder.Build());
}

static async Task PdfTextExtraction()
{
    using var input = PdfFixture(true);
    using var forward = new ForwardOnlyStream(input.ToArray());
    var result = await new PdfToMarkdownConverter().ConvertAsync(forward, ConversionOptions.Default, CancellationToken.None);
    AssertEx.Contains("PDF conversion works", result.Markdown);
    AssertEx.Contains("This is selectable PDF text.", result.Markdown);
    AssertEx.Contains("\\*literal\\*", result.Markdown);
    AssertEx.Equal(1, result.PageCount);
}

static async Task PdfOcrFallback()
{
    using var input = PdfFixture(true, true);
    var recognizer = new FixtureRecognizer();
    var result = await new PdfToMarkdownConverter().ConvertAsync(input, ConversionOptions.Default, CancellationToken.None, recognizer);
    AssertEx.Equal("2", string.Join(",", recognizer.Pages));
    AssertEx.Contains("This is selectable PDF text.", result.Markdown);
    AssertEx.Contains("Recovered page 2", result.Markdown);
    AssertEx.Contains("local OCR", result.Markdown);
    AssertEx.True(result.Markdown.IndexOf("Recovered page 2", StringComparison.Ordinal) > result.Markdown.IndexOf("selectable PDF text", StringComparison.Ordinal), "Page order was not preserved.");
}

static async Task OxpsOcrFallback()
{
    var fixture = new OxpsFixtureBuilder();
    fixture.AddPage().Glyph("Original text", 50, 100);
    fixture.AddPage();
    var recognizer = new FixtureRecognizer();
    using var input = fixture.Build();
    var result = await new OxpsToMarkdownConverter().ConvertAsync(input, ConversionOptions.Default, new MemoryAssetSink(), CancellationToken.None, recognizer);
    AssertEx.Equal("2", string.Join(",", recognizer.Pages));
    AssertEx.Contains("Original text", result.Markdown);
    AssertEx.Contains("Recovered page 2", result.Markdown);
}

static async Task UnreadableJobsFail()
{
    foreach (var pdf in new[] { true, false })
    {
        using var input = pdf ? PdfFixture(false) : new OxpsFixtureBuilder().Build();
        try
        {
            var recognizer = new FixtureRecognizer { Empty = true };
            if (pdf) await new PdfToMarkdownConverter().ConvertAsync(input, ConversionOptions.Default, CancellationToken.None, recognizer);
            else await new OxpsToMarkdownConverter().ConvertAsync(input, ConversionOptions.Default, new MemoryAssetSink(), CancellationToken.None, recognizer);
            throw new Exception("A completely textless job must not report success.");
        }
        catch (ConversionException exception)
        {
            AssertEx.Equal(ConversionFailure.NoExtractableText, exception.Failure);
        }
    }
}

static async Task PdfFailureAndCancellation()
{
    using var invalid = new MemoryStream(new byte[] { 1, 2, 3 });
    try
    {
        await new PdfToMarkdownConverter().ConvertAsync(invalid, ConversionOptions.Default, CancellationToken.None);
        throw new Exception("Invalid PDF should fail.");
    }
    catch (ConversionException exception) { AssertEx.Equal(ConversionFailure.InvalidPdf, exception.Failure); }
    using var valid = PdfFixture(true);
    using var canceled = new CancellationTokenSource();
    canceled.Cancel();
    await AssertEx.ThrowsAsync<OperationCanceledException>(() => new PdfToMarkdownConverter().ConvertAsync(valid, ConversionOptions.Default, canceled.Token));
}

static async Task ContentCounts()
{
    var text = new OxpsFixtureBuilder();
    text.AddPage().Glyph("One", 60, 100).Glyph("Two", 60, 120).Glyph("Three", 60, 140);
    var textResult = await Convert(text);
    AssertEx.Equal(3, textResult.GlyphRunCount);
    AssertEx.Equal(0, textResult.GlyphRunsWithoutText);
    AssertEx.Equal(0, textResult.ImageCount);

    var png = System.Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
    var image = new OxpsFixtureBuilder();
    image.AddPngResource("Resources/Images/pixel.png", png);
    image.AddPage().Image("/Resources/Images/pixel.png", 0, 0, 800, 800);
    var imageResult = await Convert(image);
    AssertEx.Equal(0, imageResult.GlyphRunCount);
    AssertEx.Equal(1, imageResult.ImageCount);
}

static async Task DeclinedImagesAreOmitted()
{
    var png = System.Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
    var fixture = new OxpsFixtureBuilder();
    fixture.AddPngResource("Resources/Images/pixel.png", png);
    fixture.AddPage().Image("/Resources/Images/pixel.png", 0, 0, 800, 800);
    using var stream = fixture.Build();
    var result = await new OxpsToMarkdownConverter().ConvertAsync(stream, ConversionOptions.Default, new DecliningAssetSink(), CancellationToken.None);

    AssertEx.Equal(0, result.Assets.Count);
    AssertEx.True(!result.Markdown.Contains("!["), "A declined image must not be linked from the Markdown." + Environment.NewLine + result.Markdown);
    AssertEx.Contains("was omitted", result.Markdown);
    AssertEx.True(result.Warnings.Any(warning => warning.Code == "image-omitted"), "Expected an image-omitted warning.");
}

static async Task ForwardOnlyInput()
{
    var fixture = new OxpsFixtureBuilder();
    fixture.AddPage().Glyph("Forward stream", 50, 100);
    using var package = fixture.Build();
    using var stream = new ForwardOnlyStream(package.ToArray());
    var result = await new OxpsToMarkdownConverter().ConvertAsync(stream, ConversionOptions.Default, new MemoryAssetSink(), CancellationToken.None);
    AssertEx.Contains("Forward stream", result.Markdown);
}

static async Task MalformedPackage()
{
    using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });
    await AssertEx.ThrowsAsync<ConversionException>(async () =>
        await new OxpsToMarkdownConverter().ConvertAsync(stream, ConversionOptions.Default, new MemoryAssetSink(), CancellationToken.None));
}

static async Task ConversionFailureCodes()
{
    foreach (var scenario in new[] { ConversionFailure.InvalidPackage, ConversionFailure.InvalidXml, ConversionFailure.MissingPart })
    {
        var fixture = new OxpsFixtureBuilder();
        fixture.AddPage().Glyph("Diagnostic fixture", 50, 100);
        using var stream = scenario == ConversionFailure.InvalidPackage
            ? new MemoryStream(new byte[] { 1, 2, 3 })
            : fixture.Build();
        if (scenario != ConversionFailure.InvalidPackage)
        {
            using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Update, true))
            {
                const string page = "Documents/1/Pages/1.fpage";
                archive.GetEntry(page)!.Delete();
                if (scenario == ConversionFailure.InvalidXml)
                {
                    using var writer = new StreamWriter(archive.CreateEntry(page).Open());
                    writer.Write("<broken");
                }
            }
            stream.Position = 0;
        }

        try
        {
            await new OxpsToMarkdownConverter().ConvertAsync(stream, ConversionOptions.Default, new MemoryAssetSink(), CancellationToken.None);
            throw new Exception("Expected conversion failure: " + scenario);
        }
        catch (ConversionException exception)
        {
            AssertEx.Equal(scenario, exception.Failure);
        }
    }
}

static async Task InterleavedPrintPackage()
{
    var fixture = new OxpsFixtureBuilder();
    var png = System.Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
    fixture.AddPngResource("Resources/Images/pixel.png", png);
    fixture.AddPage().Glyph("Interleaved café 漢字", 50, 100);
    fixture.AddPage().Glyph("Second printed page", 50, 100).Image("/Resources/Images/pixel.png", 50, 150, 10, 10);
    using var original = fixture.Build();
    using var interleaved = new MemoryStream();
    using (var source = new System.IO.Compression.ZipArchive(original, System.IO.Compression.ZipArchiveMode.Read, true))
    using (var target = new System.IO.Compression.ZipArchive(interleaved, System.IO.Compression.ZipArchiveMode.Create, true))
    {
        foreach (var entry in source.Entries.Reverse())
        {
            using var bytes = new MemoryStream();
            using (var input = entry.Open()) input.CopyTo(bytes);
            var data = bytes.ToArray();
            if (entry.FullName.EndsWith("2.fpage", StringComparison.Ordinal))
            {
                using var whole = target.CreateEntry(entry.FullName).Open();
                whole.Write(data, 0, data.Length);
                continue;
            }
            // Deliberately split inside XML and UTF-8 characters, with >10 pieces
            // and reverse ZIP order to catch lexicographic piece sorting.
            var count = (data.Length + 6) / 7;
            for (var index = count - 1; index >= 0; index--)
            {
                var suffix = index == count - 1 ? "].last.piece" : "].piece";
                using var output = target.CreateEntry(entry.FullName + "/[" + index + suffix).Open();
                output.Write(data, index * 7, Math.Min(7, data.Length - index * 7));
            }
        }
    }
    interleaved.Position = 0;
    using var forward = new ForwardOnlyStream(interleaved.ToArray());
    var sink = new MemoryAssetSink();
    var result = await new OxpsToMarkdownConverter().ConvertAsync(forward, ConversionOptions.Default, sink, CancellationToken.None);
    AssertEx.Contains("Interleaved café 漢字", result.Markdown);
    AssertEx.Contains("Second printed page", result.Markdown);
    AssertEx.Equal(2, result.PageCount);
    AssertEx.Equal(1, sink.Assets.Count);
}

static async Task InvalidInterleavedPieces()
{
    foreach (var names in new[]
    {
        new[] { "part/[1].last.piece" }, // Missing first piece.
        new[] { "part/[0].piece", "part/[2].last.piece" }, // Gap.
        new[] { "part/[0].piece" }, // Missing final marker.
        new[] { "part/[0].last.piece", "part/[1].last.piece" }, // Multiple final markers.
        new[] { "part/[0].piece", "part/[0].last.piece" }, // Duplicate index.
        new[] { "part", "part/[0].last.piece" }, // Whole and split same part.
    })
    {
        using var stream = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, true))
            foreach (var name in names) zip.CreateEntry(name);
        stream.Position = 0;
        try
        {
            await new OxpsToMarkdownConverter().ConvertAsync(stream, ConversionOptions.Default, new MemoryAssetSink(), CancellationToken.None);
            throw new Exception("Expected invalid piece sequence to fail.");
        }
        catch (ConversionException exception)
        {
            AssertEx.Equal(ConversionFailure.InvalidPackage, exception.Failure);
        }
    }
}

static async Task MissingImageWarning()
{
    var fixture = new OxpsFixtureBuilder();
    fixture.AddPage().Image("/Resources/Images/missing.png", 0, 0, 100, 100);
    var result = await Convert(fixture);
    AssertEx.True(result.Warnings.Any(warning => warning.Code == "missing-image"), "Expected a missing-image warning.");
}

static async Task Cancellation()
{
    var fixture = new OxpsFixtureBuilder();
    fixture.AddPage().Glyph("Canceled", 50, 100);
    using var stream = fixture.Build();
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    await AssertEx.ThrowsAsync<OperationCanceledException>(async () =>
        await new OxpsToMarkdownConverter().ConvertAsync(stream, ConversionOptions.Default, new MemoryAssetSink(), cancellation.Token));
}

static async Task OptionValidation()
{
    var options = ConversionOptions.Default;
    options.MarginFraction = 0.5;
    using var stream = new MemoryStream();
    await AssertEx.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        await new OxpsToMarkdownConverter().ConvertAsync(stream, options, new MemoryAssetSink(), CancellationToken.None));
}

static async Task<ConversionResult> Convert(OxpsFixtureBuilder fixture)
{
    using var stream = fixture.Build();
    return await new OxpsToMarkdownConverter().ConvertAsync(stream, ConversionOptions.Default, new MemoryAssetSink(), CancellationToken.None);
}

internal sealed class FixtureRecognizer : IPageTextRecognizer
{
    public List<int> Pages { get; } = new();
    public bool Empty { get; set; }
    public Task<IReadOnlyList<RecognizedTextLine>> RecognizeAsync(int pageNumber, double width, double height, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Pages.Add(pageNumber);
        IReadOnlyList<RecognizedTextLine> lines = Empty ? Array.Empty<RecognizedTextLine>()
            : new[] { new RecognizedTextLine("Recovered page " + pageNumber, 50, 100, 200, 12) };
        return Task.FromResult(lines);
    }
}

internal sealed class DecliningAssetSink : IAssetSink
{
    public Task<AssetReference?> WriteAsync(AssetContent asset, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<AssetReference?>(null);
    }
}

internal sealed class ForwardOnlyStream : MemoryStream
{
    public ForwardOnlyStream(byte[] bytes)
        : base(bytes)
    {
    }

    public override bool CanSeek => false;

    public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException();

    public override long Position
    {
        get => base.Position;
        set => throw new NotSupportedException();
    }
}
