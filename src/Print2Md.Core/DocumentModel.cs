using System;
using System.Collections.Generic;

namespace Print2Md.Core;

internal sealed class XpsDocumentModel
{
    public List<XpsPageModel> Pages { get; } = new List<XpsPageModel>();
}

internal sealed class XpsPageModel
{
    public int Number { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public List<TextRunModel> TextRuns { get; } = new List<TextRunModel>();

    public List<ImageModel> Images { get; } = new List<ImageModel>();
}

internal sealed class TextRunModel
{
    public string Text { get; set; } = string.Empty;

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double FontSize { get; set; }

    public bool Bold { get; set; }

    public bool Italic { get; set; }

    public string? NavigateUri { get; set; }
}

internal sealed class ImageModel
{
    public byte[] Bytes { get; set; } = Array.Empty<byte>();

    public string ContentType { get; set; } = "application/octet-stream";

    public string ContentHash { get; set; } = string.Empty;

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public AssetReference? Reference { get; set; }
}

internal sealed class TextLine
{
    public List<TextRunModel> Runs { get; } = new List<TextRunModel>();

    public double Baseline { get; set; }

    public double Left { get; set; }

    public double Right { get; set; }

    public double FontSize { get; set; }

    public bool Bold { get; set; }

    public bool Italic { get; set; }

    public string PlainText { get; set; } = string.Empty;

    public string MarkdownText { get; set; } = string.Empty;

    public double Top => Baseline - FontSize;
}

internal sealed class MarkdownBlock
{
    public MarkdownBlock(double top, string markdown)
    {
        Top = top;
        Markdown = markdown;
    }

    public double Top { get; }

    public string Markdown { get; }
}

internal readonly struct Matrix2D
{
    public Matrix2D(double m11, double m12, double m21, double m22, double offsetX, double offsetY)
    {
        M11 = m11;
        M12 = m12;
        M21 = m21;
        M22 = m22;
        OffsetX = offsetX;
        OffsetY = offsetY;
    }

    public static Matrix2D Identity => new Matrix2D(1, 0, 0, 1, 0, 0);

    public double M11 { get; }
    public double M12 { get; }
    public double M21 { get; }
    public double M22 { get; }
    public double OffsetX { get; }
    public double OffsetY { get; }

    public (double X, double Y) Transform(double x, double y) =>
        ((x * M11) + (y * M21) + OffsetX, (x * M12) + (y * M22) + OffsetY);

    public Matrix2D Append(Matrix2D child) => new Matrix2D(
        (M11 * child.M11) + (M12 * child.M21),
        (M11 * child.M12) + (M12 * child.M22),
        (M21 * child.M11) + (M22 * child.M21),
        (M21 * child.M12) + (M22 * child.M22),
        (OffsetX * child.M11) + (OffsetY * child.M21) + child.OffsetX,
        (OffsetX * child.M12) + (OffsetY * child.M22) + child.OffsetY);
}

