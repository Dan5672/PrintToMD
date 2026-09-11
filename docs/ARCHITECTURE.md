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
        ├── OmittedAssetSink
        │     declines every image
        │
        └── WriteTextAsync(target)
```

The MSIX declaration uses `PreferredInputFormat="application/oxps"` and `OutputFileTypes="md"`. Windows owns destination selection and supplies both the OXPS stream and selected `StorageFile` to the background task.

## Conversion rules

- OXPS parts are resolved from package relationships; XML parsing prohibits DTDs and external resolution.
- Glyph runs are grouped by transformed baselines, then joined using their measured or estimated advance widths.
- Body font size is the character-weighted document median. Larger short lines become Markdown headings.
- A glyph-level horizontal gutter separates prose columns. A bold first aligned row is required before the same geometry is treated as a Markdown table.
- Margin text is normalized for whitespace and changing digits. It is removed only when it occurs in the outer 10% on at least three pages and 60% of the document.
- A sink may decline an image by returning `null`. The image is then omitted, marked in the Markdown with an HTML comment, and reported as an `image-omitted` warning.
- Image-only pages receive a Markdown HTML comment plus a structured warning; no OCR is attempted.

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

The print system grants the background task access to the `StorageFile` the user named in the Save As dialog and to nothing else. `GetParentAsync` on that file returns `null`, so the task cannot create sibling files: neither an asset folder nor a temporary file to move over the target. Markdown is therefore written directly to the granted file, and `OmittedAssetSink` declines every image.

Restoring images requires a way to write beside the target file, such as the restricted `broadFileSystemAccess` capability, or a change in output format such as embedding images as `data:` URIs.

Cancellation reports `Canceled`; parsing, conversion, and I/O failures report `Failed`. A failure after the write has begun can leave a partially written Markdown file, because the target is written in place.

## Security and privacy

- No network capability is declared in the package manifest.
- No dynamic code, subprocess, printer-driver DLL, or spooler plug-in is used.
- Package XML parsing disables DTD processing and external XML resolution.
- Diagnostics exclude document content, job names, usernames, and file paths.
- The package targets the Windows 11 driverless virtual-printer API and does not rely on legacy v3/v4 print drivers.

