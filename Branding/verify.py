"""Validate the generated images without building or installing the app."""

from pathlib import Path
import re
import unittest
import xml.etree.ElementTree as ET

from PIL import Image

from export import ASSETS, HERE, ROOT, SCALES, TARGETS, scaled, target_filename


class ArtworkTests(unittest.TestCase):
    def test_scale_sizes(self):
        families = {
            "Square44x44Logo": (44, 44),
            "Square150x150Logo": (150, 150),
            "SmallTile": (71, 71),
            "LargeTile": (310, 310),
            "Wide310x150Logo": (310, 150),
            "SplashScreen": (620, 300),
            "StoreLogo": (50, 50),
        }
        for name, base in families.items():
            for scale in SCALES:
                with self.subTest(name=name, scale=scale):
                    with Image.open(ASSETS / f"{name}.scale-{scale}.png") as image:
                        self.assertEqual(image.size, tuple(scaled(v, scale) for v in base))
                        self.assertEqual(image.mode, "RGBA")
                        self.assertEqual(image.getpixel((0, 0))[3], 0)
                        self.assertGreater(image.getchannel("A").getextrema()[1], 0)

    def test_target_sizes_and_qualifier_uniqueness(self):
        seen = set()
        for path in ASSETS.glob("Square44x44Logo.*targetsize*.png"):
            target = int(re.search(r"targetsize-(\d+)", path.name)[1])
            form = re.search(r"altform-([a-z]+)", path.name)
            key = (target, form[1] if form else None)
            self.assertNotIn(key, seen, f"Duplicate resource qualifiers: {path.name}")
            seen.add(key)
        for target in TARGETS:
            for form in (None, "unplated", "lightunplated"):
                with self.subTest(target=target, form=form):
                    with Image.open(ASSETS / target_filename(target, form)) as image:
                        self.assertEqual(image.size, (target, target))
                        self.assertEqual(image.getpixel((target // 2, target // 2))[3], 255)

    def test_ico_frames(self):
        with Image.open(ASSETS / "WindowIcon.ico") as image:
            self.assertEqual(image.ico.sizes(), {(s, s) for s in set(TARGETS) | {128}})
            for size in image.ico.sizes():
                self.assertEqual(image.ico.getimage(size).size, size)

    def test_wordmark_is_preserved(self):
        with Image.open(HERE / "wordmark-mask.png") as mask:
            for theme in ("dark", "light"):
                with Image.open(HERE / f"readme-{theme}.png") as header:
                    self.assertEqual(header.size, (1134, 265))
                    box = (300, 0, 1134, 265)
                    self.assertEqual(header.getchannel("A").crop(box).tobytes(), mask.crop(box).tobytes())
                    self.assertEqual(header.getpixel((0, 0))[3], 0)

    def test_manifest_logo_references_resolve(self):
        tree = ET.parse(ROOT / "FluentDL" / "Package.appxmanifest")
        names = {"Logo", "Square150x150Logo", "Square44x44Logo", "Wide310x150Logo",
                 "Square71x71Logo", "Square310x310Logo", "Image"}
        for node in tree.iter():
            refs = [v for k, v in node.attrib.items() if k in names and v.startswith("Assets\\")]
            if node.tag.endswith("}Logo") and node.text:
                refs.append(node.text)
            for ref in refs:
                path = ROOT / "FluentDL" / Path(ref.replace("\\", "/"))
                self.assertTrue(path.exists() or list(path.parent.glob(path.stem + ".scale-*.png")), ref)

    def test_preview_and_readme_images_exist(self):
        preview = (HERE / "preview.html").read_text(encoding="utf-8")
        for src in re.findall(r'src="([^"]+)"', preview):
            self.assertTrue((HERE / src).is_file(), src)
        readme = (ROOT / "README.md").read_text(encoding="utf-8")
        header = ET.fromstring(readme.split("</h1>", 1)[0] + "</h1>")
        links = header.findall("a")
        self.assertEqual(len(links), 2)
        for theme in ("dark", "light"):
            matching = [link for link in links if link.attrib["href"].endswith(f"#gh-{theme}-mode-only")]
            self.assertEqual(len(matching), 1)
            images = matching[0].findall("img")
            self.assertEqual(len(images), 1)
            self.assertEqual(images[0].attrib["src"], f"./Branding/readme-{theme}.png")
            self.assertTrue((ROOT / images[0].attrib["src"]).is_file())
        self.assertNotIn("prefers-color-scheme", readme)

    def test_master_is_self_contained(self):
        root = ET.parse(HERE / "fluentdl.svg").getroot()
        self.assertEqual(root.attrib["viewBox"], "0 0 256 256")
        for node in root.iter():
            for key, value in node.attrib.items():
                if key.endswith("href"):
                    self.assertFalse(value.startswith(("http:", "https:", "file:")), value)


if __name__ == "__main__":
    unittest.main(verbosity=2)
