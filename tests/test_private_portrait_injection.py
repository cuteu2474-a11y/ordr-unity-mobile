"""Offline tests for the public-code/private-art distinction."""

import importlib.util
import struct
import tempfile
import unittest
import zipfile
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("private_portraits", ROOT / "tools/import_private_portraits.py")
PORTRAITS = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PORTRAITS)


def png_chunk(kind: bytes, contents: bytes) -> bytes:
    return (struct.pack(">I", len(contents)) + kind + contents +
            struct.pack(">I", zlib.crc32(kind + contents) & 0xFFFFFFFF))


def tiny_png() -> bytes:
    header = struct.pack(">IIBBBBB", 1, 1, 8, 2, 0, 0, 0)
    raw_pixel = b"\x00\xff\x00\x00"
    return (PORTRAITS.PNG_SIGNATURE + png_chunk(b"IHDR", header) +
            png_chunk(b"IDAT", zlib.compress(raw_pixel)) + png_chunk(b"IEND", b""))


class PrivatePortraitInjectionTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.repo = self.root / "public-project"
        self.repo.mkdir()
        (self.repo / ".gitignore").write_text("Assets/Resources/CharacterPortraits/\n")
        self.bundle = self.root / "private-images.zip"

    def write_zip(self, *, omit=None, corrupt=None, extra=False):
        with zipfile.ZipFile(self.bundle, "w") as archive:
            for number in range(1, 10):
                if number != omit:
                    archive.writestr(f"C{number}.png", b"not-a-png" if number == corrupt else tiny_png())
            if extra:
                archive.writestr("../../unwanted.png", tiny_png())

    def test_injects_nine_local_sprites_without_extracting_untrusted_zip_members(self):
        self.write_zip(extra=True)
        destinations = PORTRAITS.inject(self.bundle, self.repo)
        self.assertEqual(9, len(destinations))
        self.assertEqual({f"C-{n:02d}.png" for n in range(1, 10)},
                         {path.name for path in destinations})
        self.assertTrue(all(path.read_bytes() == tiny_png() for path in destinations))
        self.assertFalse((self.root / "unwanted.png").exists())
        self.assertEqual(destinations, PORTRAITS.inject(self.bundle, self.repo))

    def test_partial_catalog_bundle_is_allowed_without_fabricating_missing_images(self):
        self.write_zip(omit=4)
        destinations = PORTRAITS.inject(self.bundle, self.repo)
        self.assertEqual(8, len(destinations))
        self.assertFalse((self.repo / PORTRAITS.RESOURCE_PATH / "C-04.png").exists())

    def test_high_grade_catalog_images_and_hyphenated_names(self):
        with zipfile.ZipFile(self.bundle, "w") as archive:
            archive.writestr("TR24.png", tiny_png())
            archive.writestr("E-09.png", tiny_png())
            archive.writestr("../../../outside.png", tiny_png())
        destinations = PORTRAITS.inject(self.bundle, self.repo)
        self.assertEqual({"TR-24.png", "E-09.png"}, {path.name for path in destinations})
        self.assertFalse((self.root / "outside.png").exists())

    def test_duplicate_character_images_rejected_before_writing(self):
        with zipfile.ZipFile(self.bundle, "w") as archive:
            archive.writestr("C1.png", tiny_png())
            archive.writestr("C-01.png", tiny_png())
        with self.assertRaises(ValueError):
            PORTRAITS.inject(self.bundle, self.repo)
        self.assertFalse((self.repo / PORTRAITS.RESOURCE_PATH).exists())

    def test_corrupted_image_does_not_partially_inject(self):
        self.write_zip(corrupt=9)
        with self.assertRaises(ValueError):
            PORTRAITS.inject(self.bundle, self.repo)
        self.assertFalse((self.repo / PORTRAITS.RESOURCE_PATH).exists())

    def test_refuses_source_inside_public_repository(self):
        source = self.repo / "private.zip"
        source.touch()
        with self.assertRaises(ValueError):
            PORTRAITS.inject(source, self.repo)

    def test_refuses_repository_without_ignore_rule(self):
        self.write_zip()
        (self.repo / ".gitignore").write_text("*.log\n")
        with self.assertRaises(ValueError):
            PORTRAITS.inject(self.bundle, self.repo)


if __name__ == "__main__":
    unittest.main()
