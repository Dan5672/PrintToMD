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
