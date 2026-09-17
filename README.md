# Print to Markdown

<p align="center">
  <img src="src/Print2Md.App/Assets/Source/Logo.png" alt="Print to Markdown logo" width="144">
</p>

<p align="center">
  Turn anything you can print in Windows into a local Markdown file.
</p>

<p align="center">
  <a href="https://github.com/Dan5672/PrintToMD/actions/workflows/build.yml"><img src="https://github.com/Dan5672/PrintToMD/actions/workflows/build.yml/badge.svg" alt="Build status"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue.svg" alt="MIT license"></a>
</p>

Print to Markdown is a Windows 11 virtual printer. Select it from an application's normal **Print** dialog and it creates a readable `.md` file instead of paper or a PDF.

The conversion runs entirely on your PC. Your documents are not uploaded, and the app does not require an internet connection while printing.

> [!IMPORTANT]
> Print to Markdown is currently a developer preview. It requires Windows 11 24H2 or later, and there is not yet a publicly trusted installer. To install it you sign a build with a development certificate created on your own PC, as described below. Visual Studio is not required for this.

## What you get

Printing a document as `meeting-notes.md` produces a single `meeting-notes.md` file containing its text.

Print to Markdown can reconstruct:

- Paragraphs and headings
- Bold and italic text
- Bulleted and numbered lists
- Simple tables
- Common two-column layouts
- Visible URLs and explicit hyperlinks
- Repeated page-header, footer, and page-number removal

> [!NOTE]
> This preview converts text only. Windows grants the converter access to the single file you name in the **Save As** dialog and to nothing else, so it cannot write images into a companion folder beside that file. Each image is left out and marked in the Markdown with an HTML comment, so nothing disappears silently.

## Requirements

To install and run the current preview, you need:

- Windows 11 version 24H2, build 26100 or later
- An x64 PC

The virtual-printer API is not available on Windows 10 or older Windows 11 releases.

To build from source you also need Visual Studio 2022 with:

- **Universal Windows Platform development** workload
- Windows 11 SDK `10.0.26100.0`
- MSIX Packaging Tools

## Install the current preview

Windows only installs an MSIX that carries a signature it trusts, so installing the preview means signing a build with a certificate you create locally. `scripts\Sign-Package.ps1` handles the whole sequence.

### 1. Download a build

Open the [most recent successful build](https://github.com/Dan5672/PrintToMD/actions/workflows/build.yml), download the `Print2Md-unsigned-x64` artifact, and extract it. Use the `.msix` for version `1.0.1.0` or later, which includes support for interleaved Windows print packages and specific conversion failure diagnostics.

### 2. Sign and install it

```powershell
git clone https://github.com/Dan5672/PrintToMD.git
cd PrintToMD
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Sign-Package.ps1 -PackagePath <path to the .msix> -Install
```

The script creates a development certificate whose subject matches the package publisher, downloads `signtool.exe` if the Windows SDK is not installed, signs the package, verifies the signature, and installs it. Omit `-PackagePath` to use the newest `.msix` under `artifacts` or your Downloads folder.

The first run stops and asks you to trust the new certificate, which needs an elevated prompt:

```powershell
Import-Certificate -FilePath .tools\Print2Md-development.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

Run that once from an administrator PowerShell, then run the script again. Later builds need only the script.

### 3. Confirm the printer

Open any application's **Print** dialog and confirm that **Print to Markdown** is listed.

### Build it yourself instead

1. Open `PRINT2MD.sln` in Visual Studio 2022.
2. Select the **Debug** and **x64** configuration.
3. In Solution Explorer, right-click `Print2Md.App` and choose **Set as Startup Project**.
4. Restore NuGet packages and build the solution.
5. Press **F5** to deploy and launch the app. Windows may ask you to enable Developer Mode.

## Use Print to Markdown

1. Open the document, webpage, email, or other content you want to convert.
2. Open the application's **Print** dialog, commonly with <kbd>Ctrl</kbd> + <kbd>P</kbd>.
3. Select **Print to Markdown** as the printer.
4. Choose the page range, A4 or Letter paper, and portrait or landscape orientation if needed.
5. Select **Print**.
6. In the Windows **Save As** dialog, choose a location and a filename ending in `.md`.
7. Open the resulting file in any Markdown editor or viewer.

Images in the printout are omitted from the Markdown and marked with an HTML comment where they appeared.

## Understand the limitations

A print job contains positioned text and page graphics, not the source document's original structure. Print to Markdown therefore has to infer headings, paragraphs, columns, and tables from the rendered page.

- Complex or borderless tables may be emitted as ordinary paragraphs.
- Unusual columns, writing directions, or layered graphics may not retain their intended reading order.
- Page layout will not be reproduced pixel-for-pixel; the goal is readable Markdown.
- Images are not saved. Pages without extractable text are rendered and processed with local Windows OCR, including scanned pages and text drawn as vector outlines. OCR requires an installed Windows recognition language and can misread characters or layout; affected pages are marked for review. A job with no recovered text reports failure instead of silently producing an empty document.
- Content that an application does not include in its print output cannot be recovered.

For the best result, use the application's standard print layout and avoid options such as multiple source pages per printed sheet.

## Privacy

- Conversion happens locally on the PC.
- The package declares no network capability.
- Document text, filenames, usernames, and selected paths are not written to diagnostic logs.
- If a conversion fails, the log contains a UTC timestamp, processing stage, fixed failure code, package version, and exception types and Windows error codes. Exception messages are excluded because they can contain document part names.

## Troubleshooting

### The printer does not appear

Confirm that the PC is running Windows 11 build 26100 or later, and that the package installed with a trusted signature:

```powershell
Get-AppxPackage -Name Print2Md | Select-Object SignatureKind, Status
```

A package can install successfully and still fail to create its printer queue. If `Get-Printer` does not list **Print to Markdown**, uninstall the package, restart the PC, and install it again.

### The Markdown contains no images

This is expected in the current preview. Images are replaced by an HTML comment recording that one was omitted.

### A scanned document has no selectable text

Version 1.0.6.0 extracts text directly from PDF passthrough and uses Windows OCR on pages without extractable text. If the app reports `OcrUnavailable`, install the appropriate Windows language/OCR component. If it reports `NoExtractableText`, neither extraction nor OCR could recover readable text.

### Windows reports that the print job failed

Print to Markdown shows a notification when conversion fails. A content-free diagnostic entry is also written to:

```text
%LOCALAPPDATA%\Packages\<Print2Md package family>\LocalState\print2md.log
```

Please include the diagnostic entry from that file when [opening a bug report](https://github.com/Dan5672/PrintToMD/issues/new). New builds distinguish unsupported input (`UnsupportedFormat`), invalid ZIP data (`InvalidPackage`), invalid XML (`InvalidXml`), missing document structure (`MissingSequence`, `MissingPart`, `MissingPageRoot`), and documents without pages (`NoPages`). Older builds only report `ConversionException`, which cannot identify which of these failures occurred. Do not attach a private source document unless you intentionally want to share it.

### Uninstall the preview

Open **Settings > Apps > Installed apps**, find **Print to Markdown**, and select **Uninstall**. Removing the package also removes its virtual-printer queue.

## Project status and development

Every push and pull request to `main` runs the converter test suite and compiles an unsigned x64 application package on GitHub Actions. Successful workflow runs provide a `Print2Md-unsigned-x64` artifact for 14 days, which you can install by signing it as described above.

A release that installs without each user trusting a certificate still requires a publicly trusted code-signing certificate, such as one issued through Azure Trusted Signing, or distribution through the Microsoft Store.

Run the platform-independent converter tests locally with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Core.ps1
```

After installing the package on Windows, exercise the actual printer with a synthetic text page:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Printer.ps1
```

This checks the generated Markdown for the expected text. The default output is `.tools\printer-smoke.md`; use `-OutputPath` with a fresh filename for another run. The test refuses to overwrite an existing file. If Windows displays a Save As dialog, choose the specified output path.

The repository contains three production projects:

- `Print2Md.Core` - OXPS parser, layout analysis, and Markdown rendering
- `Print2Md.Tasks` - background component invoked by the Windows print system
- `Print2Md.App` - packaged app, virtual-printer declaration, capabilities, and status UI

See [Architecture](docs/ARCHITECTURE.md) for the data flow, conversion rules, and failure model.

Useful Microsoft documentation:

- [Print Support Virtual Printer manifest](https://learn.microsoft.com/windows-hardware/drivers/devapps/msix-manifest-specification-print-support-virtual-printer)
- [Print Support App v4 design guide](https://learn.microsoft.com/windows-hardware/drivers/devapps/print-support-app-v4-design-guide)
- [Virtual printer session API](https://learn.microsoft.com/uwp/api/windows.graphics.printing.workflow.printworkflowvirtualprintersession)

## License

Print to Markdown is available under the [MIT License](LICENSE).
