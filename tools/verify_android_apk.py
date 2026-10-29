"""Check the built APK before upload; this does not certify device gameplay."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import zipfile


REQUIRED = (
    'AndroidManifest.xml',
    'classes.dex',
    'assets/bin/Data/globalgamemanagers',
    'lib/arm64-v8a/libil2cpp.so',
    'lib/arm64-v8a/libunity.so',
    'lib/armeabi-v7a/libil2cpp.so',
    'lib/armeabi-v7a/libunity.so',
)


def required_runtime_assets():
    # Read the importer's single source list; do not maintain a second roster.
    importer = Path(__file__).resolve().parents[1] / 'Assets/Editor/OrdrCharacterDataSync.cs'
    source = importer.read_text(encoding='utf-8')
    match = re.search(r'\bDataFiles\s*=\s*\{(.*?)\};', source, re.S)
    files = re.findall(r'"([^"\n]+\.json)"', match.group(1)) if match else []
    if not files or len(files) != len(set(files)):
        raise ValueError('Cannot read the unique runtime resource list from the importer')
    return ['Assets/Resources/OrdrData/' + name for name in files]


def verify_runtime_manifest(path, digest):
    manifest_path = path.with_suffix('.build.json')
    try:
        manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
    except (OSError, ValueError) as error:
        raise ValueError('Unity runtime data build manifest is missing or invalid') from error
    if not isinstance(manifest, dict) or manifest.get('formatVersion') != 1:
        raise ValueError('Unsupported Unity runtime data build manifest')
    if manifest.get('apkSha256') != digest:
        raise ValueError('Unity build manifest does not match this APK SHA-256')
    assets = manifest.get('runtimeAssets')
    expected = required_runtime_assets()
    if (not isinstance(assets, list) or any(not isinstance(item, str) for item in assets)
            or len(assets) != len(set(assets)) or set(assets) != set(expected)):
        raise ValueError('Unity build manifest must contain every required runtime asset exactly once')
    return assets


def inspect_apk(path):
    path = Path(path)
    if path.suffix.lower() != '.apk' or not path.is_file():
        raise ValueError('APK file is missing: ' + str(path))
    try:
        with zipfile.ZipFile(path) as apk:
            entries = apk.infolist()
            names = [entry.filename for entry in entries]
            if len(names) != len(set(names)):
                raise ValueError('APK contains duplicate ZIP entries')
            missing = [name for name in REQUIRED
                       if name not in names or apk.getinfo(name).file_size == 0]
            if missing:
                raise ValueError('APK payload missing or empty: ' + ', '.join(missing))
            if apk.testzip() is not None:
                raise ValueError('APK ZIP integrity check failed')
    except zipfile.BadZipFile as error:
        raise ValueError('APK is not a valid ZIP container') from error
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(block)
    assets = verify_runtime_manifest(path, digest.hexdigest())
    return {'file': path.name, 'bytes': path.stat().st_size,
            'runtimeAssets': assets, 'runtimeDataVerifiedBy': 'Unity BuildReport.packedAssets',
            'sha256': digest.hexdigest(), 'abis': ['arm64-v8a', 'armeabi-v7a']}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('apk', type=Path)
    parser.add_argument('--apksigner', required=True, type=Path)
    parser.add_argument('--commit', required=True)
    args = parser.parse_args()
    if not re.fullmatch(r'[0-9a-f]{40}', args.commit):
        parser.error('--commit must be the full source commit SHA')
    result = inspect_apk(args.apk)
    # Android's actual verifier, including v2/v3 signing. A ZIP or fabricated
    # META-INF certificate alone is insufficient proof of an installable APK.
    subprocess.run([str(args.apksigner.resolve()), 'verify', '--verbose',
                    str(args.apk.resolve())], check=True)
    result.update(sourceCommit=args.commit, signatureVerified=True,
                  deviceGameplayVerified=False)
    report = args.apk.with_suffix('.verification.json')
    report.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    args.apk.with_suffix('.apk.sha256').write_text(
        result['sha256'] + '  ' + args.apk.name + '\n', encoding='ascii')
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    main()
