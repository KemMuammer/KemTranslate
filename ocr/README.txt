Place the Tesseract OCR runtime files in this folder to bundle OCR with the app.

Required:
- tesseract.exe
- tessdata\eng.traineddata (or the languages you want to support)

Recommended layout:
ocr\
  tesseract.exe
  *.dll
  tessdata\
    eng.traineddata
    deu.traineddata

Build behavior:
- Everything under this `ocr` folder is copied to the app output directory.
- The app will first look for `ocr\tesseract.exe` next to the built executable.

Notes:
- If you bundle Tesseract, include its required license/notice files in your distribution.
- If this folder is empty, the app falls back to a configured path or a system installation.
