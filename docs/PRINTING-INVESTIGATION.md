# Windows printing investigation — 12 September 2026

## Findings

The original installed version contained the parent-folder access requirement removed in `a9bc232`. Its log recorded `System.IO.IOException 0x80131620`. A subsequent installed build included that fix but failed with `Print2Md.Core.ConversionException 0x80131500`.

The parser treated ZIP entries as complete OPC parts. Valid OXPS print packages can split a part across `name/[0].piece` through `name/[N].last.piece`. Consequently, the parser could not resolve the document sequence, document, or page when those parts were interleaved. The original tests only supplied complete parts.

The revised parser indexes logical parts, validates contiguous numeric piece sequences, and joins bytes before XML or image decoding. Mixed ordinary/interleaved parts are supported. The background task now writes to the granted target stream directly, avoiding the replace-file transaction used by `FileIO.WriteTextAsync`. Diagnostics identify the stage and failure code, without document content or exception messages.

References: [OpenXPS specification, ECMA-388](https://www.ecma-international.org/wp-content/uploads/ECMA-388_1st_edition_june_2009.pdf), [Microsoft XPS printing example](https://learn.microsoft.com/en-us/windows/win32/printdocs/print-an-xps-om), [FileIO transaction behavior](https://learn.microsoft.com/en-us/windows/apps/develop/files/best-practices-writing-files).

## Verification

- Before the parser change, an interleaved-package regression fixture failed with “The OXPS package has no FixedDocumentSequence relationship.”
- All 13 converter tests passed after the fix. Coverage includes reverse ZIP order, numeric ordering beyond ten pieces, split UTF-8 data, mixed whole/split parts, images, forward-only input, and invalid piece sequences.
- [Windows build 34666756645](https://github.com/Dan5672/PrintToMD/actions/runs/34666756645) passed for application commit `bd21166dc9f29bf89b3832df22cb0b8e3753df72`.
- A synthetic GDI print through the old installed printer reproduced `ConversionException` at `2026-09-12T02:09:09Z`.
- The signed `1.0.1.0` package was installed as an update. The same synthetic print then produced `.tools/printer-after.md`, containing `Print2Md smoke test 12345`, with no new diagnostic failure.

The generic old log cannot establish precisely which parser branch rejected the original user's document. The live before/after test verifies the repaired Windows printing path; the original document and other source applications still need user verification.

## Empty output investigation — 17 September 2026

Version 1.0.5.0 logged successful conversions with no extracted text: one job had seven pages and 41 images; the latest two-page jobs had zero glyphs, zero recognized images and only two output characters. The latter is consistent with outlined/vector content, although the original print package was not captured. The PDF input branch also only wrote a placeholder instead of extracting document text.

Version 1.0.6.0 adds PDF text extraction using PdfPig and local Windows OCR for OXPS/PDF pages without extractable text. OXPS pages are converted to PDF using the Windows print workflow converter, rendered and recognized locally. Documents that remain unreadable fail explicitly instead of reporting blank Markdown as success. OCR output carries a review notice. Images are still omitted from printer output with a notice. The synchronous background session startup fix is preserved. In-place installation no longer silently falls back to uninstalling the app.

### Verification of 1.0.6.0

- All 19 converter tests passed locally and in CI, including real PDF text extraction, mixed text/OCR page order, empty-recognition failure, malformed PDF, cancellation and existing interleaved OXPS regressions.
- [Windows build 35166464837](https://github.com/Dan5672/PrintToMD/actions/runs/35166464837) passed for application commit `5289e8a`, including UWP .NET Native compilation. PdfPig attribution is bundled in the app.
- The signed package was verified and installed as an update; Windows reports `Print2Md_1.0.6.0_x64__n696szkjxb9v8`.
- Live raster print passed at `2026-09-17T05:54:43Z`: zero glyphs, one image, OCR recovered exactly `Print2Md smoke test 12345`; output is `.tools/printer-106-raster.md` (197 characters including notices).
- A real six-page document completed at `2026-09-17T05:57:21Z`: zero glyphs, nine images, OCR recovered 32/37/37/35/66/29 lines across its pages and generated 15,477 Markdown characters. The log confirms target write, flush and successful job completion. Its saved file has not yet been inspected.
- Forced GDI `PrintToFile=true` returned Access Denied before app activation. The smoke script now uses the printer's normal Save As flow; this successfully reached the converter.
- The separate outline smoke test did not produce its requested output before timeout; it remains unverified. A real document was submitted during that check, as recorded above.

Direct PDF input is covered by converter tests; the successful live jobs above exercise the OXPS-to-PDF rendering and Windows OCR path. It has not been confirmed whether the real six-page document is the original failing PDF. OCR spelling and layout fidelity need review in the saved output.

## Paragraph reflow — 1.0.7.0

Review of `.tools/GTD.md` found paragraphs split before short final lines and wrapped bullet text detached from its item. OCR reports ink bounding-box height, which was being treated as exact font size; small height variations exceeded the paragraph continuation tolerance. Nearby OCR heights now use a prose-derived body height while substantially larger headings retain their size. List rendering now consumes indented continuation lines using the existing paragraph spacing and structural checks.

Both new regression fixtures failed before the change and pass afterward; all 21 converter tests pass. Fixtures check that real paragraph gaps and separate list items are preserved. These are synthetic OCR geometry fixtures based on the GTD symptoms; the original print geometry was not retained. Existing Markdown files are unchanged and a repeat print is needed to assess the improvement on GTD. Cross-page paragraph joining and diagram OCR remain limitations.

## Progress and clean output — 1.0.8.0

After the target is selected, a tagged Windows notification shows receiving, extraction, OCR page/total and saving stages. A File ready notification replaces it only after output is written and flushed; failure and cancellation also replace the same notification. Progress updates do not repeatedly open popups. Notifications respect Windows settings and do not block printing if unavailable. The Save As-created target can still be empty during conversion.

Conversion-status comments are removed from Markdown. Warning codes and counts are retained in diagnostics. OCR now preserves widely separated cells on a recognized line, rather than discarding horizontal geometry by creating one text run per line. Short OCR table rows no longer require bold metadata to avoid column-order splitting. A synthetic Action/Context table regression passes, along with all 22 converter tests; actual GTD table reconstruction still needs a repeat print.

Windows build `35207413081` passed for `884cf81`; the signed 1.0.8.0 package was installed in place. The live image-only table smoke test passed: `.tools/printer-108-table.md` contains a proper Action/Context table with both rows intact and no conversion comments. Windows notification history contains File ready for that exact output filename. The intermediate popup visibility still needs user confirmation; notification delivery is enabled on this machine. `scripts/Test-Printer.ps1 -Mode Table` reproduces the end-to-end test and verifies the table cells as well as the smoke text.
