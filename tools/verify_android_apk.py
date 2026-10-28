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
    'assets/bin/Data/resources.assets',
    'lib/arm64-v8a/libil2cpp.so',
    'lib/arm64-v8a/libunity.so',
    'lib/armeabi-v7a/libil2cpp.so',
    'lib/armeabi-v7a/libunity.so',
)


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
    return {'file': path.name, 'bytes': path.stat().st_size,
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
