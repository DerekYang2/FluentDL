# FluentDL artwork

`fluentdl.svg` is the master for all exports. The app uses PNG and ICO exports,
not the SVG filters at runtime.

## Regenerate

From the repository root:

```powershell
python -m pip install -r Branding/requirements.txt
python Branding/export.py
python Branding/verify.py
```

Open `Branding/preview.html` to compare the headers and Windows icon sizes on
light and dark backgrounds. Python is only needed to regenerate artwork, not
to build or run FluentDL.

## Exports

- `FluentDL/Assets/WindowIcon.ico` contains the window and executable icon sizes.
- The scale-qualified PNGs cover 100%, 125%, 150%, 200%, 250%, 300%, and 400%.
- The target-size PNGs cover 16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80,
  96, and 256 pixels, with default, unplated, and light-unplated versions.
- Tile images have more padding than unplated taskbar icons. All backgrounds
  are transparent, and the disc center is opaque in both themes.
- `logo.png` is a transparent 1024-pixel square for general use.
- `store-icon-300.png` and `store-icon-150.png` are separate Store listing icon
  exports. Use the size requested by Partner Center. These are not substitutes
  for the scale-qualified package logos.
- `social-preview.png` is a 1280 by 640 GitHub repository social-preview image.
- `favicon.ico` contains 16, 32, 48, and 64-pixel icons for a website.
- `readme-dark.png` and `readme-light.png` are the README headers for dark and
  light themes.

## README themes

The root README uses GitHub's `#gh-dark-mode-only` and `#gh-light-mode-only`
image suffixes. This follows GitHub's selected theme rather than relying only
on the browser's `prefers-color-scheme`. Other Markdown viewers may show both
images if they do not support GitHub's theme suffixes.
