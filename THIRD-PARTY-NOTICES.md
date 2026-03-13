# Third-Party Notices

This repository may be distributed with optional third-party dependencies, especially for OCR support.

## Tesseract OCR

`KemTranslate` can use Tesseract OCR when the runtime files are placed in the `ocr/` folder or configured externally.

If you distribute `KemTranslate` with bundled Tesseract files:

- include the original Tesseract license files from the Tesseract distribution you used
- include any additional notices required by the exact binaries and language data you ship
- keep those notices together with the bundled OCR files in your release package

## Important

This repository does not embed third-party license text automatically for external OCR binaries.

You are responsible for including the correct upstream license and notice files for any third-party binaries you distribute with your release.
