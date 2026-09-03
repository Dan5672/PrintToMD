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
> Print to Markdown is currently a developer preview. It requires Windows 11 24H2 or later, and there is not yet a signed public installer. The current version must be deployed from Visual Studio as described below. GitHub Actions artifacts are unsigned build outputs and cannot be installed directly by most users.

## What you get

Printing a document as `meeting-notes.md` produces:

```text
meeting-notes.md
meeting-notes.assets/
  00a4d8c7b5d1f91d3f1a7b26.png
  d20f11e1fb7155ea40c0eeb9.jpg
```

The Markdown file contains relative links to the extracted images. Keep the `.md` file and its `.assets` folder together when moving or sharing the document.

Print to Markdown can reconstruct:

- Paragraphs and headings
- Bold and italic text
- Bulleted and numbered lists
- Simple tables
- Common two-column layouts
- Visible URLs and explicit hyperlinks
- PNG, JPEG, and GIF images
- Repeated page-header, footer, and page-number removal

## Requirements

To run the current preview, you need:

- Windows 11 version 24H2, build 26100 or later
- An x64 PC
- Visual Studio 2022 with:
  - **Universal Windows Platform development** workload
  - Windows 11 SDK `10.0.26100.0`
  - MSIX Packaging Tools

The virtual-printer API is not available on Windows 10 or older Windows 11 releases.

## Install the current preview

### 1. Download the source

Either use **Code > Download ZIP** on this repository and extract it, or clone it with Git:

```powershell
git clone https://github.com/Dan5672/PrintToMD.git
cd PrintToMD
```

### 2. Deploy it from Visual Studio

1. Open `PRINT2MD.sln` in Visual Studio 2022.
2. Select the **Debug** and **x64** configuration.
3. In Solution Explorer, right-click `Print2Md.App` and choose **Set as Startup Project**.
4. Restore NuGet packages and build the solution.
5. Press **F5** to deploy and launch the app. Windows may ask you to enable Developer Mode.
6. Confirm that **Print to Markdown** appears in a Windows Print dialog.

Visual Studio installs the app package and registers the virtual-printer queue for the current development environment.

## Use Print to Markdown

1. Open the document, webpage, email, or other content you want to convert.
2. Open the application's **Print** dialog, commonly with <kbd>Ctrl</kbd> + <kbd>P</kbd>.
3. Select **Print to Markdown** as the printer.
4. Choose the page range, A4 or Letter paper, and portrait or landscape orientation if needed.
5. Select **Print**.
6. In the Windows **Save As** dialog, choose a location and a filename ending in `.md`.
7. Open the resulting file in any Markdown editor or viewer.

If the printout contains images, Print to Markdown creates a companion `<filename>.assets` folder in the same location.

## Understand the limitations

A print job contains positioned text and page graphics, not the source document's original structure. Print to Markdown therefore has to infer headings, paragraphs, columns, and tables from the rendered page.

- Complex or borderless tables may be emitted as ordinary paragraphs.
- Unusual columns, writing directions, or layered graphics may not retain their intended reading order.
- Page layout will not be reproduced pixel-for-pixel; the goal is readable Markdown.
- Scans and image-only pages are preserved as images, but **OCR is not performed**. A warning is added to the Markdown so this is not mistaken for lost text.
- Content that an application does not include in its print output cannot be recovered.

For the best result, use the application's standard print layout and avoid options such as multiple source pages per printed sheet.

## Privacy

- Conversion happens locally on the PC.
- The package declares no network capability.
- Document text, filenames, usernames, and selected paths are not written to diagnostic logs.
- If a conversion fails, the log contains only a UTC timestamp, exception type, and Windows error code.

## Troubleshooting

### The printer does not appear

Confirm that the PC is running Windows 11 build 26100 or later and that Visual Studio successfully deployed `Print2Md.App`. Stop the debugging session, rebuild the solution, and press **F5** again.

### Images are missing in the Markdown viewer

Keep the generated `.assets` folder beside the Markdown file. If you rename the Markdown file and its asset folder, you must also update the relative image paths inside the Markdown.

### A scanned document has no selectable text

This is expected in the current preview. Scanned pages are retained as images, and the app does not perform OCR.

### Windows reports that the print job failed

Print to Markdown shows a notification when conversion fails. A content-free diagnostic entry is also written to:

```text
%LOCALAPPDATA%\Packages\<Print2Md package family>\LocalState\print2md.log
```

Please include the exception type and error code from that file when [opening a bug report](https://github.com/Dan5672/PrintToMD/issues/new). Do not attach a private source document unless you intentionally want to share it.

### Uninstall the preview

Open **Settings > Apps > Installed apps**, find **Print to Markdown**, and select **Uninstall**. Removing the package also removes its virtual-printer queue.

## Project status and development

Every push and pull request to `main` runs the converter test suite and compiles an unsigned x64 application package on GitHub Actions. Successful workflow runs provide a `Print2Md-unsigned-x64` artifact for 14 days. The artifact verifies the complete project build, but a distributable release still requires a trusted code-signing certificate.

Run the platform-independent converter tests locally with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Core.ps1
```

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
