import hashlib
from pathlib import Path
import tempfile
import unittest
import zipfile

from tools.verify_android_apk import REQUIRED, inspect_apk


class AndroidApkVerificationTests(unittest.TestCase):
    # These are container fixtures, never represented as real Unity builds.
    def make_apk(self, directory, omit=None, empty=None):
        path = Path(directory) / 'fixture.apk'
        with zipfile.ZipFile(path, 'w') as apk:
            for name in REQUIRED:
                if name != omit:
                    apk.writestr(name, b'' if name == empty else b'fixture payload')
        return path

    def test_container_report_hashes_the_actual_file(self):
        with tempfile.TemporaryDirectory() as directory:
            apk = self.make_apk(directory)
            result = inspect_apk(apk)
            self.assertEqual(hashlib.sha256(apk.read_bytes()).hexdigest(), result['sha256'])
            self.assertEqual(apk.stat().st_size, result['bytes'])
            self.assertNotIn('signatureVerified', result)

    def test_each_required_payload_is_mandatory(self):
        with tempfile.TemporaryDirectory() as directory:
            for name in REQUIRED:
                with self.subTest(missing=name), self.assertRaises(ValueError):
                    inspect_apk(self.make_apk(directory, omit=name))

    def test_empty_native_library_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(ValueError):
                inspect_apk(self.make_apk(directory, empty='lib/arm64-v8a/libil2cpp.so'))

    def test_corrupted_zip_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'broken.apk'
            path.write_bytes(b'not an apk')
            with self.assertRaises(ValueError):
                inspect_apk(path)

    def test_valid_zip_with_corrupted_payload_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = self.make_apk(directory)
            # ZIP_STORED fixture: change payload without changing its CRC.
            path.write_bytes(path.read_bytes().replace(b'fixture payload', b'damaged payload', 1))
            with self.assertRaises(ValueError):
                inspect_apk(path)

    def test_missing_file_and_app_bundle_are_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(ValueError):
                inspect_apk(Path(directory) / 'missing.apk')
            path = self.make_apk(directory).rename(Path(directory) / 'fixture.aab')
            with self.assertRaises(ValueError):
                inspect_apk(path)
