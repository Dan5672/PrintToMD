using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace Print2Md.Core;

internal sealed class OxpsPackageReader
{
    private const string FixedRepresentationRelationship = "fixedrepresentation";
    private static readonly Regex NumberRegex = new Regex(@"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?", RegexOptions.Compiled);
    private readonly List<ConversionWarning> warnings;

    public OxpsPackageReader(List<ConversionWarning> warnings)
    {
        this.warnings = warnings;
    }

    public async Task<XpsDocumentModel> ReadAsync(Stream source, CancellationToken cancellationToken)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        Stream packageStream = source;
        MemoryStream? ownedCopy = null;
        if (!source.CanSeek)
        {
            ownedCopy = new MemoryStream();
            await source.CopyToAsync(ownedCopy, 81920, cancellationToken).ConfigureAwait(false);
            ownedCopy.Position = 0;
            packageStream = ownedCopy;
        }

        try
        {
            using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, true))
            {
                var entries = archive.Entries.ToDictionary(
                    entry => NormalizePartName(entry.FullName),
                    entry => entry,
                    StringComparer.OrdinalIgnoreCase);
                var contentTypes = ReadContentTypes(entries);
                var sequencePart = FindFixedDocumentSequence(entries);
                var document = new XpsDocumentModel();

                foreach (var documentPart in ReadDocumentReferences(entries, sequencePart))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    foreach (var pagePart in ReadPageReferences(entries, documentPart))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var page = ReadPage(entries, contentTypes, pagePart, document.Pages.Count + 1);
                        document.Pages.Add(page);
                    }
                }

                if (document.Pages.Count == 0)
                {
                    throw new ConversionException("The OXPS package does not contain any fixed pages.");
                }

                return document;
            }
        }
        catch (ConversionException)
        {
            throw;
        }
        catch (InvalidDataException exception)
        {
            throw new ConversionException("The print job is not a valid OXPS package.", exception);
        }
        catch (XmlException exception)
        {
            throw new ConversionException("The OXPS package contains invalid XML.", exception);
        }
        finally
        {
            ownedCopy?.Dispose();
        }
    }

    private static ContentTypeMap ReadContentTypes(IReadOnlyDictionary<string, ZipArchiveEntry> entries)
    {
        var map = new ContentTypeMap();
        if (!entries.TryGetValue("[Content_Types].xml", out var entry))
        {
            return map;
        }

        var xml = LoadXml(entry);
        foreach (var element in xml.Root?.Elements() ?? Enumerable.Empty<XElement>())
        {
            if (element.Name.LocalName == "Default")
            {
                var extension = Attribute(element, "Extension");
                var contentType = Attribute(element, "ContentType");
                if (!string.IsNullOrEmpty(extension) && !string.IsNullOrEmpty(contentType))
                {
                    map.Defaults[extension!] = contentType!;
                }
            }
            else if (element.Name.LocalName == "Override")
            {
                var part = NormalizePartName(Attribute(element, "PartName") ?? string.Empty);
                var contentType = Attribute(element, "ContentType");
                if (!string.IsNullOrEmpty(part) && !string.IsNullOrEmpty(contentType))
                {
                    map.Overrides[part] = contentType!;
                }
            }
        }

        return map;
    }

    private static string FindFixedDocumentSequence(IReadOnlyDictionary<string, ZipArchiveEntry> entries)
    {
        if (entries.TryGetValue("_rels/.rels", out var rootRelationships))
        {
            var xml = LoadXml(rootRelationships);
            var relationship = xml.Descendants().FirstOrDefault(element =>
                element.Name.LocalName == "Relationship" &&
                (Attribute(element, "Type") ?? string.Empty).IndexOf(FixedRepresentationRelationship, StringComparison.OrdinalIgnoreCase) >= 0);
            var target = relationship == null ? null : Attribute(relationship, "Target");
            if (!string.IsNullOrWhiteSpace(target))
            {
                return ResolvePartName(string.Empty, target!);
            }
        }

        var fallback = entries.Keys.FirstOrDefault(name => name.EndsWith(".fdseq", StringComparison.OrdinalIgnoreCase));
        if (fallback == null)
        {
            throw new ConversionException("The OXPS package has no FixedDocumentSequence relationship.");
        }

        return fallback;
    }

    private static IEnumerable<string> ReadDocumentReferences(IReadOnlyDictionary<string, ZipArchiveEntry> entries, string sequencePart)
    {
        var xml = LoadRequiredXml(entries, sequencePart);
        foreach (var reference in xml.Descendants().Where(element => element.Name.LocalName == "DocumentReference"))
        {
            var source = Attribute(reference, "Source");
            if (!string.IsNullOrWhiteSpace(source))
            {
                yield return ResolvePartName(sequencePart, source!);
            }
        }
    }

    private static IEnumerable<string> ReadPageReferences(IReadOnlyDictionary<string, ZipArchiveEntry> entries, string documentPart)
    {
        var xml = LoadRequiredXml(entries, documentPart);
        foreach (var reference in xml.Descendants().Where(element => element.Name.LocalName == "PageContent"))
        {
            var source = Attribute(reference, "Source");
            if (!string.IsNullOrWhiteSpace(source))
            {
                yield return ResolvePartName(documentPart, source!);
            }
        }
    }

    private XpsPageModel ReadPage(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        ContentTypeMap contentTypes,
        string pagePart,
        int pageNumber)
    {
        var xml = LoadRequiredXml(entries, pagePart);
        var root = xml.Root ?? throw new ConversionException($"Fixed page {pageNumber} has no root element.");
        var page = new XpsPageModel
        {
            Number = pageNumber,
            Width = ParseDouble(Attribute(root, "Width"), 816),
            Height = ParseDouble(Attribute(root, "Height"), 1056),
        };

        foreach (var glyph in root.DescendantsAndSelf().Where(element => element.Name.LocalName == "Glyphs"))
        {
            var text = DecodeUnicodeString(Attribute(glyph, "UnicodeString"));
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var fontSize = Math.Max(1, ParseDouble(Attribute(glyph, "FontRenderingEmSize"), 12));
            var x = ParseDouble(Attribute(glyph, "OriginX"), 0);
            var y = ParseDouble(Attribute(glyph, "OriginY"), 0);
            var transform = GetAccumulatedTransform(glyph);
            var origin = transform.Transform(x, y);
            var fontUri = Attribute(glyph, "FontUri") ?? string.Empty;
            var simulations = Attribute(glyph, "StyleSimulations") ?? string.Empty;
            var width = MeasureTextWidth(text, fontSize, Attribute(glyph, "Indices"));
            var endpoint = transform.Transform(x + width, y);

            page.TextRuns.Add(new TextRunModel
            {
                Text = text,
                X = Math.Min(origin.X, endpoint.X),
                Y = Math.Min(origin.Y, endpoint.Y),
                Width = Math.Max(Math.Abs(endpoint.X - origin.X), fontSize * 0.25),
                FontSize = fontSize * ApproximateScale(transform),
                Bold = ContainsAny(fontUri, "bold", "black", "semibold", "demi") || simulations.IndexOf("Bold", StringComparison.OrdinalIgnoreCase) >= 0,
                Italic = ContainsAny(fontUri, "italic", "oblique") || simulations.IndexOf("Italic", StringComparison.OrdinalIgnoreCase) >= 0,
                NavigateUri = FindNavigateUri(glyph),
            });
        }

        foreach (var imageBrush in root.DescendantsAndSelf().Where(element => element.Name.LocalName == "ImageBrush"))
        {
            var source = ParseImageSource(Attribute(imageBrush, "ImageSource"));
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            var imagePart = ResolvePartName(pagePart, source!);
            if (!entries.TryGetValue(imagePart, out var imageEntry))
            {
                warnings.Add(new ConversionWarning("missing-image", "An image resource could not be found in the print package.", pageNumber));
                continue;
            }

            var bytes = ReadAllBytes(imageEntry);
            var bounds = GetImageBounds(imageBrush, page.Width, page.Height);
            var transform = GetAccumulatedTransform(imageBrush);
            var topLeft = transform.Transform(bounds.X, bounds.Y);
            var bottomRight = transform.Transform(bounds.X + bounds.Width, bounds.Y + bounds.Height);
            page.Images.Add(new ImageModel
            {
                Bytes = bytes,
                ContentHash = Hashing.Sha256(bytes),
                ContentType = contentTypes.GetContentType(imagePart),
                X = Math.Min(topLeft.X, bottomRight.X),
                Y = Math.Min(topLeft.Y, bottomRight.Y),
                Width = Math.Abs(bottomRight.X - topLeft.X),
                Height = Math.Abs(bottomRight.Y - topLeft.Y),
            });
        }

        return page;
    }

    private static Matrix2D GetAccumulatedTransform(XElement element)
    {
        var transform = Matrix2D.Identity;
        foreach (var ancestor in element.AncestorsAndSelf().Reverse())
        {
            var value = Attribute(ancestor, "RenderTransform");
            if (string.IsNullOrWhiteSpace(value))
            {
                var property = ancestor.Elements().FirstOrDefault(child => child.Name.LocalName.EndsWith(".RenderTransform", StringComparison.Ordinal));
                value = property?.DescendantsAndSelf().FirstOrDefault(child => child.Name.LocalName == "MatrixTransform")?.Attributes()
                    .FirstOrDefault(attribute => attribute.Name.LocalName == "Matrix")?.Value;
            }

            if (TryParseMatrix(value, out var local))
            {
                transform = transform.Append(local);
            }
        }

        return transform;
    }

    private static bool TryParseMatrix(string? value, out Matrix2D matrix)
    {
        matrix = Matrix2D.Identity;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var numbers = ParseNumbers(value!);
        if (numbers.Count != 6)
        {
            return false;
        }

        matrix = new Matrix2D(numbers[0], numbers[1], numbers[2], numbers[3], numbers[4], numbers[5]);
        return true;
    }

    private static (double X, double Y, double Width, double Height) GetImageBounds(XElement imageBrush, double pageWidth, double pageHeight)
    {
        var viewport = ParseNumbers(Attribute(imageBrush, "Viewport") ?? string.Empty);
        if (viewport.Count == 4)
        {
            return (viewport[0], viewport[1], Math.Abs(viewport[2]), Math.Abs(viewport[3]));
        }

        var path = imageBrush.Ancestors().FirstOrDefault(element => element.Name.LocalName == "Path");
        var data = path == null ? null : Attribute(path, "Data");
        var coordinates = ParseNumbers(data ?? string.Empty);
        if (coordinates.Count >= 4)
        {
            var xs = new List<double>();
            var ys = new List<double>();
            for (var index = 0; index + 1 < coordinates.Count; index += 2)
            {
                xs.Add(coordinates[index]);
                ys.Add(coordinates[index + 1]);
            }

            return (xs.Min(), ys.Min(), Math.Max(1, xs.Max() - xs.Min()), Math.Max(1, ys.Max() - ys.Min()));
        }

        return (0, 0, pageWidth, pageHeight);
    }

    private static double MeasureTextWidth(string text, double fontSize, string? indices)
    {
        if (!string.IsNullOrWhiteSpace(indices))
        {
            double total = 0;
            var foundAdvance = false;
            foreach (var token in indices!.Split(';'))
            {
                var value = token.Trim();
                var closingCluster = value.IndexOf(')');
                if (closingCluster >= 0)
                {
                    value = value.Substring(closingCluster + 1);
                }

                var fields = value.Split(',');
                if (fields.Length > 1 && double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var advance))
                {
                    total += advance * fontSize / 100.0;
                    foundAdvance = true;
                }
            }

            if (foundAdvance && total > 0)
            {
                return total;
            }
        }

        return Math.Max(fontSize * 0.25, text.Length * fontSize * 0.52);
    }

    private static string? FindNavigateUri(XElement element)
    {
        foreach (var current in element.AncestorsAndSelf())
        {
            var uri = current.Attributes().FirstOrDefault(attribute =>
                attribute.Name.LocalName == "NavigateUri" ||
                attribute.Name.LocalName.EndsWith(".NavigateUri", StringComparison.Ordinal))?.Value;
            if (!string.IsNullOrWhiteSpace(uri))
            {
                return uri;
            }
        }

        return null;
    }

    private static string? ParseImageSource(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value!.Trim();
        if (value.StartsWith("{ColorConvertedBitmap", StringComparison.OrdinalIgnoreCase))
        {
            var payload = value.Substring("{ColorConvertedBitmap".Length).Trim().TrimEnd('}');
            return payload.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        }

        return value.StartsWith("{}", StringComparison.Ordinal) ? value.Substring(2) : value;
    }

    private static string DecodeUnicodeString(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value!.StartsWith("{}", StringComparison.Ordinal) ? value.Substring(2) : value;
    }

    private static double ApproximateScale(Matrix2D matrix)
    {
        var xScale = Math.Sqrt((matrix.M11 * matrix.M11) + (matrix.M12 * matrix.M12));
        var yScale = Math.Sqrt((matrix.M21 * matrix.M21) + (matrix.M22 * matrix.M22));
        var scale = (xScale + yScale) / 2.0;
        return scale <= 0 ? 1 : scale;
    }

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0);

    private static List<double> ParseNumbers(string value)
    {
        var numbers = new List<double>();
        foreach (Match match in NumberRegex.Matches(value))
        {
            if (double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                numbers.Add(parsed);
            }
        }

        return numbers;
    }

    private static double ParseDouble(string? value, double fallback) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private static XDocument LoadRequiredXml(IReadOnlyDictionary<string, ZipArchiveEntry> entries, string partName)
    {
        if (!entries.TryGetValue(NormalizePartName(partName), out var entry))
        {
            throw new ConversionException($"The OXPS package is missing required part '{partName}'.");
        }

        return LoadXml(entry);
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        using (var stream = entry.Open())
        using (var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
        }))
        {
            return XDocument.Load(reader, LoadOptions.None);
        }
    }

    private static byte[] ReadAllBytes(ZipArchiveEntry entry)
    {
        using (var input = entry.Open())
        using (var output = new MemoryStream())
        {
            input.CopyTo(output);
            return output.ToArray();
        }
    }

    private static string ResolvePartName(string basePart, string target)
    {
        target = Uri.UnescapeDataString(target.Replace('\\', '/'));
        if (target.StartsWith("/", StringComparison.Ordinal))
        {
            return NormalizePartName(target);
        }

        var separator = basePart.LastIndexOf('/');
        var baseDirectory = separator < 0 ? string.Empty : basePart.Substring(0, separator + 1);
        var segments = (baseDirectory + target).Split('/');
        var resolved = new List<string>();
        foreach (var segment in segments)
        {
            if (string.IsNullOrEmpty(segment) || segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (resolved.Count > 0)
                {
                    resolved.RemoveAt(resolved.Count - 1);
                }

                continue;
            }

            resolved.Add(segment);
        }

        return string.Join("/", resolved);
    }

    private static string NormalizePartName(string value) => value.Replace('\\', '/').TrimStart('/');

    private static string? Attribute(XElement element, string localName) =>
        element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == localName)?.Value;

    private sealed class ContentTypeMap
    {
        public Dictionary<string, string> Defaults { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> Overrides { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string GetContentType(string partName)
        {
            if (Overrides.TryGetValue(NormalizePartName(partName), out var contentType))
            {
                return contentType;
            }

            var extension = Path.GetExtension(partName).TrimStart('.');
            if (Defaults.TryGetValue(extension, out contentType))
            {
                return contentType;
            }

            switch (extension.ToLowerInvariant())
            {
                case "png": return "image/png";
                case "jpg":
                case "jpeg": return "image/jpeg";
                case "gif": return "image/gif";
                case "tif":
                case "tiff": return "image/tiff";
                case "wdp":
                case "jxr": return "image/vnd.ms-photo";
                default: return "application/octet-stream";
            }
        }
    }
}
