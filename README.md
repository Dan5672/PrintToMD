# Print2Md

[![Build](https://github.com/Dan5672/PrintToMD/actions/workflows/build.yml/badge.svg)](https://github.com/Dan5672/PrintToMD/actions/workflows/build.yml)

Print2Md is a Windows 11 virtual printer that converts print jobs into readable Markdown. It uses the modern Print Support Virtual Printer API instead of a legacy v3/v4 printer driver.

When an application prints to **Print to Markdown**, Windows displays its native Save As dialog. Print2Md receives the job as OXPS, reconstructs document structure locally, and writes:

```text
document.md
document.assets/
  00a4d8...png
  d20f11...jpg
```

No document content is uploaded, and no network connection is required while printing.

## Current capabilities

- Paragraph, heading, bold, italic, list, simple-table, and two-column reconstruction.
- Explicit hyperlink and visible URL preservation.
- Conservative removal of repeated page headers, footers, and page numbers.
- JPEG, PNG, and GIF extraction; other Windows-decodable image formats are converted to PNG.
- Image-only page preservation with an explicit `OCR was not performed` warning.
- Atomic Markdown replacement and content-addressed companion assets.
- Local, content-free failure logging in the app data directory.

Print data is rendered page geometry, not the source document. Complex tables, unusual writing directions, layered graphics, and exact source semantics cannot always be recovered. OCR and cloud/AI enhancement are intentionally out of scope for this MVP.

## Requirements

- Windows 11 version 24H2, build 26100 or newer.
- x64 processor for the current package configuration.
- Visual Studio 2022 with:
  - Universal Windows Platform development workload.
  - Windows 11 SDK `10.0.26100.0`.
  - MSIX Packaging Tools.
- .NET 8 SDK to run the platform-independent converter tests.

The repository has three production projects:

- `Print2Md.Core`: .NET Standard OXPS parser and Markdown renderer.
- `Print2Md.Tasks`: UWP Windows Runtime background component invoked by the print system.
- `Print2Md.App`: UWP/MSIX host, manifest, printer capabilities, and status UI.

See [Architecture](docs/ARCHITECTURE.md) for the data flow and failure model.

## Run converter tests

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Core.ps1
```

The test runner has no third-party test-framework dependency. It creates representative OXPS packages in memory and exits nonzero on failure.

## Build and install the virtual printer

1. Open `PRINT2MD.sln` in Visual Studio 2022.
2. Select `Debug` and `x64`; set `Print2Md.App` as the startup project.
3. Restore NuGet packages and build the solution.
4. For direct development deployment, press `F5`. Visual Studio deploys the package and registers **Print to Markdown**.
5. For a reusable sideload package, choose **Project → Publish → Create App Packages**, select sideloading, and create or select a certificate whose subject matches the manifest `Publisher` value.
6. Trust the generated `.cer` under **Local Machine → Trusted People**, then run the generated `Add-AppDevPackage.ps1` from the `AppPackages` directory.

The checked-in identity uses `Publisher="CN=Print2Md-Development"`. If Visual Studio creates a certificate with another subject, update the manifest Publisher to match it before packaging.

Uninstalling **Print to Markdown** from **Settings → Apps → Installed apps** removes the virtual-printer queue with the package.

## GitHub Actions build

Every push and pull request to `main` runs the converter tests and compiles an unsigned x64 application package on GitHub's `windows-2025` runner. Successful runs expose a `Print2Md-unsigned-x64` artifact for 14 days.

Unsigned CI artifacts prove that the complete project compiles, but Windows will not install them directly. Local development deployment uses Visual Studio; distributable packages require a code-signing certificate whose subject matches the package manifest. Keep the PFX and its password in GitHub Actions secrets rather than committing them.

## Use

1. Open Print in a Win32 or modern Windows application.
2. Choose **Print to Markdown**.
3. Select a `.md` destination in the Windows Save As dialog.
4. Open the Markdown file in an editor or viewer. Keep its `.assets` folder beside it so relative image links continue to work.

If conversion fails, Windows marks the print job as failed and Print2Md shows a notification. The app writes only the UTC timestamp, exception type, and HRESULT to `print2md.log`; document text and selected paths are never logged.

## References

- [Microsoft Print Support Virtual Printer manifest](https://learn.microsoft.com/windows-hardware/drivers/devapps/msix-manifest-specification-print-support-virtual-printer)
- [Print Support App v4 design guide](https://learn.microsoft.com/windows-hardware/drivers/devapps/print-support-app-v4-design-guide)
- [Virtual printer API requirements](https://learn.microsoft.com/uwp/api/windows.graphics.printing.workflow.printworkflowvirtualprintersession)
