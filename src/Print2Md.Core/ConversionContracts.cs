using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Print2Md.Core;

/// <summary>Controls the conservative, deterministic OXPS-to-Markdown conversion.</summary>
public sealed class ConversionOptions
{
    public bool RemoveRepeatedMargins { get; set; } = true;

    public double MarginFraction { get; set; } = 0.10;

    public int MinimumRepeatPages { get; set; } = 3;

    public double RepeatDocumentFraction { get; set; } = 0.60;

    public bool DetectHeadings { get; set; } = true;

    public bool DetectLists { get; set; } = true;

    public bool DetectSimpleTables { get; set; } = true;

    public bool DetectColumns { get; set; } = true;

    public static ConversionOptions Default => new ConversionOptions();
}

/// <summary>Receives an image before the converter emits its Markdown reference.</summary>
public interface IAssetSink
{
    Task<AssetReference> WriteAsync(AssetContent asset, CancellationToken cancellationToken);
}

public sealed class AssetContent
{
    public AssetContent(string contentHash, string contentType, byte[] bytes, int pageNumber)
    {
        ContentHash = contentHash ?? throw new ArgumentNullException(nameof(contentHash));
        ContentType = contentType ?? throw new ArgumentNullException(nameof(contentType));
        Bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
        PageNumber = pageNumber;
    }

    public string ContentHash { get; }

    public string ContentType { get; }

    public byte[] Bytes { get; }

    public int PageNumber { get; }
}

public sealed class AssetReference
{
    public AssetReference(string relativePath, string contentType)
    {
        RelativePath = relativePath ?? throw new ArgumentNullException(nameof(relativePath));
        ContentType = contentType ?? throw new ArgumentNullException(nameof(contentType));
    }

    public string RelativePath { get; }

    public string ContentType { get; }
}

public sealed class ConversionWarning
{
    public ConversionWarning(string code, string message, int? pageNumber = null)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        Message = message ?? throw new ArgumentNullException(nameof(message));
        PageNumber = pageNumber;
    }

    public string Code { get; }

    public string Message { get; }

    public int? PageNumber { get; }
}

public sealed class ConversionResult
{
    internal ConversionResult(string markdown, IReadOnlyList<AssetReference> assets, IReadOnlyList<ConversionWarning> warnings, int pageCount)
    {
        Markdown = markdown;
        Assets = assets;
        Warnings = warnings;
        PageCount = pageCount;
    }

    public string Markdown { get; }

    public IReadOnlyList<AssetReference> Assets { get; }

    public IReadOnlyList<ConversionWarning> Warnings { get; }

    public int PageCount { get; }
}

public sealed class ConversionException : Exception
{
    public ConversionException(string message)
        : base(message)
    {
    }

    public ConversionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

