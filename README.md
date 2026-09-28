# Print2MD

<p align="center">
  <img src="src/Print2Md.App/Assets/Source/Logo.png" alt="Print2MD" width="144">
</p>

**A Windows 11 virtual printer that turns printed content into editable Markdown.**

Choose **Print2MD** in an application's Print dialog, select a `.md` filename, and wait for **File ready**. Text extraction and OCR run locally on your PC. Documents are not uploaded.

[Builds](https://github.com/Dan5672/PrintToMD/actions/workflows/build.yml) · [Report an issue](https://github.com/Dan5672/PrintToMD/issues) · [Architecture](docs/ARCHITECTURE.md) · [License](LICENSE)

> **Store release candidate — version 1.1.0.0.** The current package targets Windows 11 24H2 or later on x64. Builds are unsigned and must be signed and trusted locally before installation. A publicly trusted installer is not yet available.

## What it does

Print documents, webpages and PDFs from applications that support Windows printing. The result is a single Markdown file, reconstructed from the text and graphics supplied by that application.

| Content | Current behavior |
| --- | --- |
| Printable text | Extracted from OXPS or PDF print data |
| Scans and text drawn as images or outlines | Recognized with local Windows OCR when a page has no extractable text |
| Paragraphs and lists | Printed lines are joined where layout permits; wrapped list items are retained |
| Headings, emphasis and links | Inferred from available text metadata; OCR cannot recover all original styling or link targets |
| Simple tables | Converted to Markdown tables; OCR support includes aligned columns, padded rows and wrapped cells |
| Repeated headers and footers | Removed when they meet the repetition and page-margin rules |
| Images and diagrams | Images are not saved; OCR may recover their text, with variable accuracy |

Markdown contains the recovered document content, without image-omission or OCR-status comments. Conversion warnings go to the diagnostic log.

## Requirements

**To print:**

- Windows 11 24H2, build 26100 or later
- An x64 PC
- A Windows OCR language installed for pages requiring recognition
- A locally trusted, signed Print2Md package

Windows 10 and older Windows 11 releases are not supported. Visual Studio is not required to install an existing build.

**To build the Windows app:** Visual Studio 2022 with the Universal Windows Platform development workload, Windows SDK `10.0.26100.0`, and MSIX packaging tools. Converter tests use the .NET SDK pinned in [global.json](global.json).

## Install or update

### 1. Get the package and signing script

Open [GitHub Actions](https://github.com/Dan5672/PrintToMD/actions/workflows/build.yml), select a successful **Build** run for the branch you intend to install, and download its `Print2MD-store-x64` artifact. Extract the artifact and locate the `.msix` file.

Artifacts are retained for 14 days. Use a successful `main` build for the current preview. Repository maintainers can start a new build using **Run workflow**; check the run's branch and package version when downloading.

Clone the repository to obtain the signing script:

```powershell
git clone https://github.com/Dan5672/PrintToMD.git
cd PrintToMD
```

The artifact also contains a Store upload file (`.msixupload` or `.appxupload`). Submit that file to Partner Center; use the separate unsigned `.msix` only for local testing.

**Preview migration:** version 1.1.0.0 uses the Store identity `FriskeLabs.Print2MD` and printer name `Print2MD`. It is a different package from the old `Print2Md` preview, not an in-place update. Complete any pending jobs before removing the old preview; check for duplicate queues when testing.

### 2. Sign and install

Run this from the repository folder, replacing the example package path:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Sign-Package.ps1 `
  -PackagePath "C:\path\to\FriskeLabs.Print2MD_1.1.0.0_x64.msix" -Install
```

The script creates or reuses a local development certificate, obtains `signtool.exe` if needed, signs a copy of the package, verifies its signature, and installs it. Downloading signing tools and timestamping the signature require network access; printing does not.

On first use, the script may stop and ask you to trust its certificate. Run the command it supplies from an **administrator PowerShell**, then rerun the signing command. For the default certificate path:

```powershell
Import-Certificate -FilePath .tools\Print2Md-development.cer `
  -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

For later updates, use the same signing certificate and a newer package version. Installation updates the existing app in place; the script does not uninstall it as a fallback. If Windows reports that the package is in use, let printing finish, close the Print2Md app, and retry.

### 3. Check the installation

```powershell
Get-AppxPackage -Name FriskeLabs.Print2MD | Select-Object Name, Version, Status
Get-Printer -Name 'Print2MD'
```

The printer should also appear in application Print dialogs.

## Print a document

1. Open the document or webpage and choose **Print** (usually `Ctrl+P`).
2. Select **Print2MD**.
3. Choose the page range, paper size and orientation. Use one source page per printed sheet for the best recognition.
4. Select **Print**, then choose a `.md` filename in Windows **Save As**.
5. Wait for **File ready** before opening the result.

**Closing the print dialog does not mean conversion has finished.** Windows can create the selected file before the converter writes its contents, so it may remain at zero bytes while processing.

A notification shows stages such as receiving the document, extracting text, recognizing page X of Y, and saving Markdown. It is replaced by **File ready** after the output has been written and flushed, or by a failure/cancellation message. Check Notification Center if the popup disappears. Windows notification settings and Do not disturb can affect visibility.

## Accuracy and limitations

Printing does not preserve the original document's full structure. Layout recovery is heuristic, and results depend on what the source application sends to Windows.

- **Review OCR text.** Small text, decorative lettering, diagrams and low-resolution scans can produce misspellings or nonsense. OCR runs only on pages without extractable text, so image text on a page that already has text may be missed.
- **Tables are not universally supported.** Simple aligned tables work, including the tested GTD table capture. Merged cells, missing cells, complex layouts and tables spanning pages remain limitations.
- **Page boundaries can split paragraphs.** Line joining within a page is improved, but text across pages can remain separated.
- **Formatting is approximate.** Columns, headings, lists and reading order may need editing. OCR loses much of the source's styling and hyperlink metadata.
- **Images are omitted.** The current printer writes one granted file and does not create a companion image folder.
- **Large jobs use memory.** Print input is buffered for extraction and optional OCR rendering. A document that remains unreadable after OCR reports failure.

The automated suite currently contains 23 tests. Live verification covers text and image printing, OCR, completion notifications, and the GTD table capture with wrapped cells. Full-page layouts and different source applications still need broader testing; see [verification notes](docs/PRINTING-INVESTIGATION.md).

## Troubleshooting

### The file is empty, or cannot be found

Wait for **File ready**. If Save As is still open, the job has not completed. Check the folder you actually selected: the filename suggested by a test script does not guarantee that Windows saved there.

If no completion notification appears, check Windows notification settings and the diagnostic log below. A canceled or failed job can leave an empty or partially written target file.

### The printer is missing

Check the Windows version and package registration using the commands in the installation section. A package can install without its printer queue appearing. If registration remains incomplete, restart Windows and check again before considering a reinstall.

### OCR fails or the output is garbled

`OcrUnavailable` means Windows could not create a recognizer. Install an appropriate Windows language/OCR component. `NoExtractableText` means extraction and OCR did not recover readable text.

For poor recognition, try a clearer source, a larger print scale, or a simpler print layout. Diagram text and complex tables may still require manual correction.

### Find diagnostic details

The log is stored at:

```text
%LOCALAPPDATA%\Packages\<Print2Md package family>\LocalState\print2md.log
```

Find the installed path with PowerShell:

```powershell
$package = Get-AppxPackage -Name FriskeLabs.Print2MD
$log = Join-Path $env:LOCALAPPDATA "Packages\$($package.PackageFamilyName)\LocalState\print2md.log"
Get-Content -LiteralPath $log -Tail 40
```

Log entries include job IDs, UTC timestamps, package version, processing stages, page counts and fixed warning/failure codes. Failure entries include exception types and error codes, but not exception messages or document content.

When [reporting a problem](https://github.com/Dan5672/PrintToMD/issues/new), include the installed version, source application, expected versus actual output, and relevant log entries. Share a source document only if you intend to make it available to others.

### Uninstall

Use **Settings > Apps > Installed apps > Print2MD > Uninstall**. Removing the package also removes its virtual-printer queue.

## Development and testing

| Project | Responsibility |
| --- | --- |
| `Print2Md.Core` | OXPS/PDF extraction, layout analysis and Markdown rendering |
| `Print2Md.Tasks` | Windows print workflow, local OCR, file writing and notifications |
| `Print2Md.App` | MSIX package, virtual-printer declaration and app UI |
| `Print2Md.Core.Tests` | Deterministic converter regression tests |

Run the converter suite:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Core.ps1
```

After installing the app, run a live printer smoke test:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Printer.ps1 `
  -Mode Table -OutputPath .tools\table-smoke.md
```

Available modes are `Text`, `Raster`, `Outline` and `Table`. Choose a fresh output filename and complete Windows Save As using that exact path. The script refuses to overwrite existing output and checks the resulting text; Table mode also checks the table cells.

To build locally, open `PRINT2MD.sln` in Visual Studio, select x64, restore dependencies, and build the app. GitHub Actions runs converter tests and produces an unsigned Release x64 MSIX on pushes/PRs to `main` and manual workflow dispatches.

`.tools/`, build output, installer packages and certificates are ignored by Git. Keep generated printouts and local signing material out of commits. Retain the source regression tests and repeatable test scripts.

See [Architecture](docs/ARCHITECTURE.md) for conversion rules and [verification notes](docs/PRINTING-INVESTIGATION.md) for reproduced failures, fixes and outstanding checks.

## Privacy and licensing

The app performs conversion locally and declares no network capability. Diagnostic logs exclude document text, document names and selected paths. Completion notifications display the output filename locally. Buffered documents are disposed after conversion; the app does not save diagnostic copies of source documents.

Print2MD is [MIT licensed](LICENSE). PDF extraction uses PdfPig; its [third-party license and notices](src/Print2Md.App/ThirdParty/PdfPig-LICENSE.txt) are included in the package.
