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
    ("missing image warning", MissingImageWarning),
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
