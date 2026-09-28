# Microsoft Store submission plan

Prepared 28 September 2026. Status: planning; no submission created or uploaded by this work.

## Proposed first release

Print2MD, Windows desktop PCs, x64, English (United States), Productivity category. Keep the current minimum Windows build 26100 (Windows 11 24H2) only after testing on that version. Terry confirmed a **free app with automatic publication when Microsoft approves**. Target markets remain to be confirmed.

Microsoft provides signing for MSIX apps distributed through the Store, so this route does not require purchasing a code-signing certificate. [Microsoft Store overview](https://learn.microsoft.com/en-us/windows/apps/publish/get-started)

## 1. Account and product identity

- Terry has created a Partner Center account. Confirm Windows app developer enrollment and any account verification are complete.
- Reserved name confirmed: **Print2MD**. Store ID: `9NS890JZ5QFS`.
- Identity confirmed: `FriskeLabs.Print2MD`; publisher `CN=7A2E1264-EDC4-4728-A5BD-D1FAC5534E2B`; publisher display name `FriskeLabs`; expected package family `FriskeLabs.Print2MD_55b83sq6keg8y`.
- Confirm Individual or Company account, public support contact and intended markets. Complete any contact or market-specific declarations Partner Center requests.
- Create the submission draft from the product overview once the name is reserved. [Submission checklist](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/create-app-submission)

Store URL (availability depends on publication): https://apps.microsoft.com/detail/9NS890JZ5QFS

## 2. Prepare the Store package

Release preparation has applied the Store identity, version **1.1.0.0**, desktop targeting and Print2MD branding. CI now uses `UapAppxPackageBuildMode=StoreUpload` with symbols. Package generation and release verification must still be completed.

- Done in source: apply the exact assigned identity and version **1.1.0.0**.
- Done in source: target `Windows.Desktop`. Still verify PC-only availability in Partner Center.
- Preserve the virtual-printer extension, OXPS preference, PDF support, Markdown output type and printer resources. This extension installs a software printer through Windows' application deployment infrastructure. [Virtual printer manifest](https://learn.microsoft.com/en-us/windows-hardware/drivers/devapps/msix-manifest-specification-print-support-virtual-printer)
- Add a Store release build using the supported Store packaging configuration. Produce an `.msixupload` or `.appxupload` with symbols, including a separate package for local testing. Microsoft recommends upload files, though plain MSIX packages are also accepted. [Upload packages](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/upload-app-packages)
- Build Release x64 with .NET Native, inspect dependencies and packaged licenses/assets, and run the Windows App Certification Kit. Keep the report with the release artifacts. [Packaging guidance](https://learn.microsoft.com/en-us/windows/msix/package/packaging-uwp-apps)
- Check Store validation of the printer extension. The current manifest declares no restricted capabilities; do not add speculative permissions. Address any actual validation findings before submission.

The Store identity will differ from the development identity. Test this transition explicitly: do not assume installation is an in-place upgrade, or that both packages can register the same printer without conflict.

## 3. Release verification gates

Existing evidence: 23 converter tests passed; installed preview successfully printed text, image-only content and the captured GTD table, and delivered notifications. See [printing investigation](PRINTING-INVESTIGATION.md). These are preview results, not certification of a future Store package.

| Test | Required evidence before submission |
| --- | --- |
| Automated suite | All converter tests pass for the exact release commit |
| Clean Windows 11 24H2 x64 | Package installs, queue appears, first print succeeds; no dependency on our developer tools |
| Current Windows 11 x64 | Same install and print checks |
| Text, webpage and PDF printing | Nonempty output, sensible paragraphs/lists, readable simple tables |
| Direct PDF input | Logs confirm PDF input rather than an OXPS job from a PDF viewer |
| Raster and outline-only pages | OCR completes; missing OCR language produces useful behavior |
| Full GTD regression | Review the entire output, beyond the previously verified table capture |
| Progress and completion | Progress remains available during conversion; File ready only after output is complete |
| Cancel, failure, large and concurrent jobs | Accurate final state, no false success, no cross-job output mix-up or hangs |
| Uninstall/reinstall/update | Correct queue registration/removal; user-saved documents remain intact; development identity transition documented |
| Certification kit | Report reviewed and actionable failures fixed |

Once a Store-distributed test package is available, also verify installation on a PC that has never trusted our development certificate.

Fix functional failures exposed by this matrix. Complex tables, cross-page reconstruction, omitted images and OCR inaccuracies can remain documented limitations; the listing must not promise lossless conversion.

## 4. Prepare listing and supporting material

- Publish a public privacy policy after checking the implementation: document processing stays local; describe user-selected outputs, local diagnostic logs and notifications accurately. Include support and privacy links in the app.
- Proposed support/website: the public GitHub repository and issue tracker. Confirm Terry's preferred public contact details.
- Capture four real screenshots using our own sample content: app instructions, choosing the printer, conversion progress, and the resulting Markdown with a simple table. Confirm current image requirements in the upload UI. Avoid private document names or third-party artwork.
- Review packaged logos at their displayed sizes and provide any additional Store artwork required.
- Complete the age-rating questionnaire from actual functionality; use its generated rating.
- Review licensing notices for included components and ensure they ship with the app.

The submission needs pricing/availability, properties, age ratings, a package and an English listing. At least one screenshot is required; Microsoft recommends four or more. [Submission checklist](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/create-app-submission)

### Draft listing description

Turn printed documents into editable Markdown, directly on your Windows PC.

Choose Print2MD in a Windows application's Print dialog, select where to save your .md file, and wait for the File ready notification. Use it for documents, webpages and PDFs from applications that support Windows printing.

- Extract printable text and reconstruct paragraphs, lists and simple tables.
- Recognize pages without extractable text using local Windows OCR.
- Follow conversion progress through Windows notifications.
- Process documents locally without uploading their contents.

Requires Windows 11 24H2 or later on an x64 PC. OCR requires a suitable installed Windows OCR language. Conversion depends on the print data supplied by the source application. Review the result for accuracy: complex layouts and tables may need editing, and images are not saved. Pages containing both text and images may omit text inside images.

## 5. Certification and publication

Upload the release package, resolve validation findings and finish all submission sections. Set the price to Free and select **Publish this submission as soon as it passes certification**, with no publication hold or future release date, as Terry requested. [Submission options](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/manage-submission-options)

### Draft notes for certification

Print2MD is a local virtual printer utility. Its main window provides instructions; conversion is invoked from other applications' Windows Print dialogs. No account, physical printer or external service is required.

On a supported Windows 11 x64 PC, install the package, open Notepad, enter a short paragraph, and print to Print2MD. Choose an .md output path in Save As. Wait for File ready in Windows notifications, then open the file in a text editor to verify the paragraph. Save As can close before conversion finishes. Further tests can print a webpage containing a simple table or an image-only page with a Windows OCR language installed.

The package uses the Windows printSupportVirtualPrinterWorkflow extension. Documents are processed locally. Images are omitted; OCR and layout reconstruction may require corrections.

### Final sequence

1. Review the exact release package, test evidence, listing, privacy policy and account settings together.
2. Submit for certification when Terry authorizes submission.
3. Address Microsoft's report if changes are requested; rebuild and retest affected behavior.
4. After approval, publish according to the agreed release setting.
5. Verify Store installation and printing, then add the Store link and installation instructions to README and tag the release.

## Immediate next steps

Terry: confirm account type and target markets. Product identity, reserved name, price and publication timing are confirmed.

Repository work: associate the Store identity, add Store packaging, prepare the privacy/support material and screenshots, then execute the release verification matrix. These are planned tasks, not work completed by this document.
