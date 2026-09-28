# Printing investigation and verification

Current package: **1.0.9.0**. Last live verification: **28 September 2026**.

## Verified behavior

- All **23 converter tests** pass, including interleaved OXPS packages, PDF extraction, OCR fallback, paragraph reflow, wrapped lists and padded tables.
- The Windows Release package built successfully in [run 35717271455](https://github.com/Dan5672/PrintToMD/actions/runs/35717271455) for application commit `a953d41`, was signed and verified locally, and installed as an in-place update.
- Live text printing recovered the expected smoke-test text.
- Live image-only printing exercised Windows' OXPS-to-PDF conversion, rendering and OCR successfully.
- A real six-page print with no extractable glyphs completed and wrote 15,477 characters.
- Progress/completion notifications were delivered; the user confirmed seeing a notification.
- The captured table from [GTD in 15 minutes](https://hamberg.no/gtd/) printed successfully through 1.0.9.0. Inspection confirmed its two-column header, all three data rows, both wrapped cell continuations, no diagnostic comments and surrounding paragraphs outside the table.

The successful GTD capture completed at `2026-09-28T00:17:15Z` and produced an 887-byte Markdown file. The log confirmed write, flush and successful job completion. A minor OCR spelling error remained in the following paragraph. This is evidence for the table capture, not proof that the full webpage or every print layout is correct.

## Findings and fixes

| Problem | Cause | Change |
| --- | --- | --- |
| Initial conversion exception | The task assumed access to the target's parent folder | Write directly to the granted target stream; omit image sidecar files |
| Valid print packages rejected | OPC parts split into numbered pieces were treated as complete ZIP entries | Resolve logical parts and validate/join pieces in order |
| Background activation stalled | Asynchronous work returned control before the printer session started | Keep activation synchronous through `session.Start()` |
| Empty output reported as success | Some jobs contained only images or outlines, and the PDF path was a placeholder | Add PDF text extraction and local OCR fallback; fail if no readable text is recovered |
| Paragraph tails and wrapped bullets detached | OCR ink height was treated as exact font size; list rendering stopped at one line | Normalize nearby OCR body heights and consume indented list continuations |
| Empty file appeared complete | Save As closed before background conversion finished | Add stage/page-count notifications and show File ready after the write is flushed and closed |
| Diagnostic comments overwhelmed the document | Each omitted image fragment emitted a comment | Keep warning codes/counts in diagnostics, outside Markdown |
| GTD table flattened | Padded rows exceeded spacing limits, narrow gaps lost column geometry, and wrapped cells interrupted detection | Retain smaller OCR gaps; use header column alignment and at least two data rows; join cell continuations before column reading-order analysis |

For the GTD regression, Windows OCR was run on a capture of the original webpage. Actual line coordinates reproduced the failure before the fix and pass afterward. Tests also preserve the following paragraph outside the table and retain native-text column behavior.

## Remaining verification and limitations

- Repeat the full GTD webpage print through 1.0.9.0 and inspect the complete output.
- Exercise direct PDF input through the installed app; current live verification has mainly used the OXPS path. Direct PDF conversion is covered by core tests.
- Test live failure/cancellation notifications and concurrent or large jobs.
- Cross-page paragraph joining, complex/merged/missing table cells and tables spanning pages remain limitations.
- OCR spelling, decorative text and diagram labels can still be inaccurate.
- The separate vector-outline smoke test has not been verified end to end.

## Reproduce tests

Use `scripts/Test-Core.ps1` for the converter suite. Use `scripts/Test-Printer.ps1` with `Text`, `Raster`, `Outline` or `Table` mode for live Windows checks. Supply a fresh output path and complete Save As with that exact path; inspect the output as well as the completion log.

Forced GDI `PrintToFile=true` returned Access Denied before app activation on the test machine. The smoke script therefore uses the printer's normal Save As flow.

Generated captures, downloaded source pages, temporary OCR scripts, smoke outputs and superseded installer builds are not repository fixtures. They were removed during cleanup. User-created printouts were preserved locally under the ignored `.tools/printouts/` directory; the latest installer and local build/signing tools were retained. Regression fixtures in `tests/` remain part of the repository.
