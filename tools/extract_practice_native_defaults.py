#!/usr/bin/env python3
"""Extract one inherited native armor mode; never grant final combat approval."""
import argparse
import hashlib
import json
from pathlib import Path
from extract_original_unit_slk_stats import parse_sylk_bytes

BASE_SHA = 'ddc360800784f808219bbc2518b16b0f12413082aada2755cc00944adcc31cec'
MAP_SHA = '0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c'


def extract(path):
    raw = path.read_bytes()
    if hashlib.sha256(raw).hexdigest() != BASE_SHA:
        raise ValueError('Unexpected base AbilityData.slk')
    if parse_sylk_bytes(raw)['AHad']['DataB1'] != '0':
        raise ValueError('Changed native devotion percentage mode')
    return {'format': 'ordr-practice-native-inheritance-v1', 'sourceW3xSha256': MAP_SHA,
            'sourceBaseAbilityDataSha256': BASE_SHA, 'AHadFlatArmorMode': 0,
            'safeForRetailVerifiedRuntime': False,
            'scope': 'Only inherited native armor-percentage mode. Does not approve target eligibility or stacking.'}


if __name__ == '__main__':
    p = argparse.ArgumentParser()
    p.add_argument('--base-ability-slk', required=True, type=Path)
    p.add_argument('--output', default=Path('data/practice-native-inherited-defaults.json'), type=Path)
    a = p.parse_args()
    a.output.write_text(json.dumps(extract(a.base_ability_slk), ensure_ascii=False, indent=2) + '\n')
