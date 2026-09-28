# Print2MD 1.1.0.0 release candidate

Built 28 September 2026 from commit `c5ecf92` on `release/store-1.1.0`.

[Successful build and downloadable artifact](https://github.com/Dan5672/PrintToMD/actions/runs/36379835192)

## Package

Local upload file: `artifacts/store-1.1.0/Print2Md.App_1.1.0.0_x64.msixupload`.

SHA-256: `B39E52A77BEE83838B2CF4C40CE321CA0E8E407164FCCA80740FF58A232AB215`.

This is the file for Partner Center's Packages section. Its filename follows the project name; its embedded Store identity is **FriskeLabs.Print2MD**, which is what determines the product association.

The sibling `_Test` directory contains a separate unsigned MSIX and framework dependencies for local installation. The Store upload and test packages are different build outputs; do not replace the upload file with the locally signed test package.

## Verified

- All 23 converter tests passed locally and on GitHub.
- Windows Release x64 Store packaging succeeded.
- Inspected the MSIX inside the upload archive: identity `FriskeLabs.Print2MD`, publisher `CN=7A2E1264-EDC4-4728-A5BD-D1FAC5534E2B`, publisher display name `FriskeLabs`, version `1.1.0.0`.
- Display name is `Print2MD`; target is `Windows.Desktop`, minimum build `26100`.
- Virtual-printer extension, OXPS/PDF formats, Markdown output type and background-task registration are present.
- Printer configuration, resource index, logos and PdfPig license are included; the upload includes symbols.

## Still required before certification submission

- Install and print through the Store-identity candidate; verify migration from the old development package and printer registration.
- Run Windows App Certification Kit. It is not installed on the current local machine, and the build workflow did not run it.
- Complete the remaining live and clean-machine tests in [the submission plan](STORE-SUBMISSION-PLAN.md).
- Finish privacy/support material, screenshots and Partner Center metadata.
- Resolve any validation findings from Partner Center.

No Store upload or certification submission has been performed. The package is available for draft upload/validation; a successful build does not establish certification readiness. The agreed release setting is free, with automatic publication after approval.
