import hashlib
import json
import os
import shutil
import subprocess
import textwrap
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


class AndroidApkWorkflowPackagingTests(unittest.TestCase):
    make_apk = AndroidApkVerificationTests.make_apk
    write_manifest = AndroidApkVerificationTests.write_manifest
    # Run the actual workflow shell block against container fixtures and a
    # signer command stub. This is a permissions/packaging test, not a real APK
    # signature, Unity build or device-play check.
    def run_workflow_package(self, signer_exit):
        repo = Path(__file__).resolve().parents[1]
        workflow = (repo / '.github/workflows/unity-editmode.yml').read_text()
        step = workflow.split('- name: APK 필수 데이터·CPU·서명·체크섬 검증', 1)[1]
        step = step.split('\n      - name:', 1)[0]
        script = textwrap.dedent(step.split('        run: |\n', 1)[1])
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            root.chmod(0o755)
            build = root / 'build/Android'
            build.mkdir(parents=True)
            apk = self.make_apk(build).rename(build / 'ORDR-Mobile-Development.apk')
            (build / 'fixture.build.json').rename(apk.with_suffix('.build.json'))
            initial_hash = hashlib.sha256(apk.read_bytes()).hexdigest()
            for name in ['tools/verify_android_apk.py', 'Assets/Editor/OrdrCharacterDataSync.cs']:
                target = root / name
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(repo / name, target)
            signer = root / 'sdk/build-tools/fixture/apksigner'
            signer.parent.mkdir(parents=True)
            signer.write_text('#!/bin/sh\nexit ' + str(signer_exit) + '\n')
            signer.chmod(0o755)
            runtime = root / 'runner-temp'
            runtime.mkdir(mode=0o777)
            runtime.chmod(0o777)
            # Reproduce Docker's root-owned, read-only-to-runner output.
            for source in build.iterdir():
                source.chmod(0o444)
            build.chmod(0o555)
            # A normal CI runner enforces these permissions. Root test hosts
            # still verify the exact shell flow leaves the source untouched.
            try:
                result = subprocess.run(['/bin/bash', '-euo', 'pipefail', '-c', script],
                    cwd=root, env={**os.environ, 'ANDROID_HOME': str(root / 'sdk'),
                                   'RUNNER_TEMP': str(runtime), 'ORDR_SOURCE_COMMIT': 'a' * 40},
                    capture_output=True, text=True)
                package = runtime / 'ordr-apk-package'
                if signer_exit:
                    self.assertNotEqual(0, result.returncode)
                    self.assertFalse(list(package.glob('*.verification.json')))
                    self.assertFalse(list(package.glob('*.sha256')))
                else:
                    self.assertEqual(0, result.returncode, result.stderr)
                    self.assertEqual({apk.name, apk.with_suffix('.build.json').name,
                                      apk.with_suffix('.verification.json').name,
                                      apk.name + '.sha256'}, {file.name for file in package.iterdir()})
                    self.assertEqual(initial_hash, hashlib.sha256((package / apk.name).read_bytes()).hexdigest())
                    self.assertEqual(apk.with_suffix('.build.json').read_bytes(),
                                     (package / apk.with_suffix('.build.json').name).read_bytes())
                    self.assertEqual(initial_hash + '  ' + apk.name + '\n',
                                     (package / (apk.name + '.sha256')).read_text())
                    report = json.loads((package / apk.with_suffix('.verification.json').name).read_text())
                    self.assertEqual(initial_hash, report['sha256'])
                    self.assertEqual('a' * 40, report['sourceCommit'])
                    self.assertFalse(report['deviceGameplayVerified'])
                    self.assertIn('path: ${{ runner.temp }}/ordr-apk-package/', workflow)
                self.assertEqual(initial_hash, hashlib.sha256(apk.read_bytes()).hexdigest())
                self.assertEqual(2, len(list(build.iterdir())))
            finally:
                build.chmod(0o755)

    def test_runner_packages_read_only_unity_output(self):
        self.run_workflow_package(signer_exit=0)

    def test_failed_signer_does_not_produce_success_reports(self):
        self.run_workflow_package(signer_exit=1)
