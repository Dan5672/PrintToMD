# Architecture

## Print workflow

```text
Application print command
        │
        ▼
Windows print system ── native Save As dialog (.md)
        │ OXPS + target StorageFile
        ▼
Print2Md.Tasks.VirtualPrinterBackgroundTask
        │
        ├── OxpsPackageReader
        │     relationships → documents → fixed pages
        │     glyphs, transforms, hyperlinks, image resources
        │
        ├── LayoutAnalyzer
        │     lines → columns/tables → paragraphs/lists/headings
        │     repeated margin suppression
        │
        ├── StagedAssetSink
        │     image normalization + SHA-256 filenames
        │
        └── sibling temporary Markdown → MoveAndReplaceAsync(target)
```

The MSIX declaration uses `PreferredInputFormat="application/oxps"` and `OutputFileTypes="md"`. Windows owns destination selection and supplies both the OXPS stream and selected `StorageFile` to the background task.

## Conversion rules

- OXPS parts are resolved from package relationships; XML parsing prohibits DTDs and external resolution.
- Glyph runs are grouped by transformed baselines, then joined using their measured or estimated advance widths.
- Body font size is the character-weighted document median. Larger short lines become Markdown headings.
- A glyph-level horizontal gutter separates prose columns. A bold first aligned row is required before the same geometry is treated as a Markdown table.
- Margin text is normalized for whitespace and changing digits. It is removed only when it occurs in the outer 10% on at least three pages and 60% of the document.
- Images are deduplicated by SHA-256. Unsupported image encodings are decoded by Windows Imaging Component and emitted as PNG.
- Image-only pages remain usable as linked images and receive a Markdown HTML comment plus a structured warning; no OCR is attempted.

The core converter exposes one asynchronous boundary:

```csharp
Task<ConversionResult> ConvertAsync(
    Stream oxps,
    ConversionOptions options,
    IAssetSink assets,
    CancellationToken cancellationToken)
```

`IAssetSink` keeps file-system and Windows Imaging APIs out of the parser, allowing deterministic in-memory tests.

## Commit and failure behavior

Images are normalized in memory before output is committed. Asset filenames are derived from source hashes, so retrying a job safely reuses the same names. The final Markdown is first written to a uniquely named sibling file and moved over the selected target only after conversion and asset writes succeed.

Cancellation reports `Canceled`; parsing, conversion, and I/O failures report `Failed`. Temporary Markdown is deleted on failure. Already committed content-addressed images can remain after a later failure, but they contain only exact assets from that job and cannot be referenced by a partial Markdown file.

## Security and privacy

- No network capability is declared in the package manifest.
- No dynamic code, subprocess, printer-driver DLL, or spooler plug-in is used.
- Package XML parsing disables DTD processing and external XML resolution.
- Diagnostics exclude document content, job names, usernames, and file paths.
- The package targets the Windows 11 driverless virtual-printer API and does not rely on legacy v3/v4 print drivers.

