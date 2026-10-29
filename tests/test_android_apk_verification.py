import hashlib
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

from tools.verify_android_apk import REQUIRED, inspect_apk, required_runtime_assets


class AndroidApkVerificationTests(unittest.TestCase):
    # These are container fixtures, never represented as real Unity builds.
    def make_apk(self, directory, omit=None, empty=None):
        path = Path(directory) / 'fixture.apk'
        with zipfile.ZipFile(path, 'w') as apk:
            for name in REQUIRED:
                if name != omit:
                    apk.writestr(name, b'' if name == empty else b'fixture payload')
        self.write_manifest(path)
        return path

    def write_manifest(self, path, **changes):
        manifest = {'formatVersion': 1,
                    'apkSha256': hashlib.sha256(path.read_bytes()).hexdigest(),
                    'runtimeAssets': required_runtime_assets()}
        manifest.update(changes)
        path.with_suffix('.build.json').write_text(json.dumps(manifest))

    def test_no_fixed_resources_filename_is_required_with_packing_evidence(self):
        with tempfile.TemporaryDirectory() as directory:
            path = self.make_apk(directory)
            with zipfile.ZipFile(path) as apk:
                self.assertNotIn('assets/bin/Data/resources.assets', apk.namelist())
            self.assertEqual(required_runtime_assets(), inspect_apk(path)['runtimeAssets'])

    def test_missing_or_invalid_manifest_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = self.make_apk(directory)
            manifest = path.with_suffix('.build.json')
            manifest.unlink()
            with self.assertRaisesRegex(ValueError, 'manifest is missing or invalid'):
                inspect_apk(path)
            manifest.write_text('broken json')
            with self.assertRaises(ValueError):
                inspect_apk(path)

    def test_manifest_from_another_apk_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = self.make_apk(directory)
            self.write_manifest(path, apkSha256='0' * 64)
            with self.assertRaisesRegex(ValueError, 'SHA-256'):
                inspect_apk(path)

    def test_each_runtime_asset_is_mandatory_in_manifest(self):
        with tempfile.TemporaryDirectory() as directory:
            path = self.make_apk(directory)
            for missing in required_runtime_assets():
                self.write_manifest(path, runtimeAssets=[asset for asset in required_runtime_assets()
                                                         if asset != missing])
                with self.subTest(missing=missing), self.assertRaises(ValueError):
                    inspect_apk(path)

    def test_duplicate_or_unknown_runtime_asset_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = self.make_apk(directory)
            assets = required_runtime_assets()
            for invalid in [assets + [assets[0]], assets + ['fake.json'], [None], None]:
                self.write_manifest(path, runtimeAssets=invalid)
                with self.subTest(invalid=invalid), self.assertRaises(ValueError):
                    inspect_apk(path)

    def test_unsupported_manifest_format_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = self.make_apk(directory)
            self.write_manifest(path, formatVersion=2)
            with self.assertRaises(ValueError):
                inspect_apk(path)

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
