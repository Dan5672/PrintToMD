using System.IO.Compression;
using System.Xml.Linq;

namespace Print2Md.Core.Tests;

internal sealed class OxpsFixtureBuilder
{
    private static readonly XNamespace Xps = "http://schemas.openxps.org/oxps/v1.0";
    private readonly List<PageFixture> pages = new();
    private readonly Dictionary<string, byte[]> resources = new(StringComparer.OrdinalIgnoreCase);

    public PageFixture AddPage(double width = 800, double height = 800)
    {
        var page = new PageFixture(pages.Count + 1, width, height);
        pages.Add(page);
        return page;
    }

    public MemoryStream Build()
    {
        if (pages.Count == 0)
        {
            AddPage();
        }

        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            WriteXml(archive, "[Content_Types].xml", CreateContentTypes());
            WriteXml(archive, "_rels/.rels", CreateRootRelationships());
            WriteXml(archive, "Documents/1/FixedDocumentSequence.fdseq", CreateSequence());
            WriteXml(archive, "Documents/1/FixedDocument.fdoc", CreateDocument());
            foreach (var page in pages)
            {
                WriteXml(archive, $"Documents/1/Pages/{page.Number}.fpage", page.Xml);
            }

            foreach (var resource in resources)
            {
                var entry = archive.CreateEntry(resource.Key);
                using var output = entry.Open();
                output.Write(resource.Value, 0, resource.Value.Length);
            }
        }

        stream.Position = 0;
        return stream;
    }

    public void AddPngResource(string partName, byte[] bytes)
    {
        resources[partName.TrimStart('/')] = bytes;
    }

    private XDocument CreateContentTypes()
    {
        XNamespace types = "http://schemas.openxmlformats.org/package/2006/content-types";
        return new XDocument(
            new XElement(types + "Types",
                new XElement(types + "Default", new XAttribute("Extension", "fdseq"), new XAttribute("ContentType", "application/vnd.ms-package.xps-fixeddocumentsequence+xml")),
                new XElement(types + "Default", new XAttribute("Extension", "fdoc"), new XAttribute("ContentType", "application/vnd.ms-package.xps-fixeddocument+xml")),
                new XElement(types + "Default", new XAttribute("Extension", "fpage"), new XAttribute("ContentType", "application/vnd.ms-package.xps-fixedpage+xml")),
                new XElement(types + "Default", new XAttribute("Extension", "png"), new XAttribute("ContentType", "image/png"))));
    }

    private static XDocument CreateRootRelationships()
    {
        XNamespace relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
        return new XDocument(
            new XElement(relationships + "Relationships",
                new XElement(relationships + "Relationship",
                    new XAttribute("Id", "r1"),
                    new XAttribute("Type", "http://schemas.microsoft.com/xps/2005/06/fixedrepresentation"),
                    new XAttribute("Target", "/Documents/1/FixedDocumentSequence.fdseq"))));
    }

    private static XDocument CreateSequence() => new(
        new XElement(Xps + "FixedDocumentSequence",
            new XElement(Xps + "DocumentReference", new XAttribute("Source", "FixedDocument.fdoc"))));

    private XDocument CreateDocument() => new(
        new XElement(Xps + "FixedDocument",
            pages.Select(page => new XElement(Xps + "PageContent", new XAttribute("Source", $"Pages/{page.Number}.fpage")))));

    private static void WriteXml(ZipArchive archive, string partName, XDocument document)
    {
        var entry = archive.CreateEntry(partName);
        using var output = entry.Open();
        document.Save(output);
    }

    internal sealed class PageFixture
    {
        private readonly XElement root;

        public PageFixture(int number, double width, double height)
        {
            Number = number;
            root = new XElement(Xps + "FixedPage", new XAttribute("Width", width), new XAttribute("Height", height));
            Xml = new XDocument(root);
        }

        public int Number { get; }

        public XDocument Xml { get; }

        public PageFixture Glyph(string text, double x, double y, double fontSize = 12, string font = "/Resources/Fonts/Regular.odttf", string? style = null, string? link = null)
        {
            var glyph = new XElement(Xps + "Glyphs",
                new XAttribute("UnicodeString", text),
                new XAttribute("OriginX", x),
                new XAttribute("OriginY", y),
                new XAttribute("FontRenderingEmSize", fontSize),
                new XAttribute("FontUri", font));
            if (style != null) glyph.Add(new XAttribute("StyleSimulations", style));
            if (link != null) glyph.Add(new XAttribute("FixedPage.NavigateUri", link));
            root.Add(glyph);
            return this;
        }

        public PageFixture Image(string source, double x, double y, double width, double height)
        {
            root.Add(
                new XElement(Xps + "Path",
                    new XAttribute("Data", $"M {x},{y} L {x + width},{y} {x + width},{y + height} {x},{y + height} Z"),
                    new XElement(Xps + "Path.Fill",
                        new XElement(Xps + "ImageBrush", new XAttribute("ImageSource", source)))));
            return this;
        }
    }
}

