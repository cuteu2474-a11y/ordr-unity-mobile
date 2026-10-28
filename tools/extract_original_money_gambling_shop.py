#!/usr/bin/env python3
"""Extract original money-gambling object costs, stock and JASS payouts."""
import argparse
import hashlib
import json
from pathlib import Path
import re

from build_original_map_base_stats import open_archive
from extract_original_unit_slk_stats import parse_sylk_bytes

MAP_SHA = '0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c'
JASS_SHA = '96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d'
BALANCE_SHA = 'f7f26217d8097b9791723feb02d9181277d9afb1947dc0a3bf553ce590ab3af5'


def sha(data):
    return hashlib.sha256(data).hexdigest()


def sections(data):
    result = {}
    current = None
    for line in data.decode('utf-8-sig').splitlines():
        if re.fullmatch(r'\[[^\]]+\]', line):
            current = result.setdefault(line[1:-1], {})
        elif current is not None and '=' in line:
            key, value = line.split('=', 1)
            current[key] = value
    return result


def extract(map_path):
    if sha(map_path.read_bytes()) != MAP_SHA:
        raise ValueError('Unexpected original W3X')
    with open_archive(map_path) as archive:
        jass_bytes = archive.read_file('war3map.j')
        balance_bytes = archive.read_file('Units\\UnitBalance.slk')
        names_bytes = archive.read_file('Units\\CampaignUnitStrings.txt')
    if sha(jass_bytes) != JASS_SHA or sha(balance_bytes) != BALANCE_SHA:
        raise ValueError('Original JASS/UnitBalance changed')
    source = jass_bytes.decode('utf-8-sig').replace('\r\n', '\n')
    balance = parse_sylk_bytes(balance_bytes)
    names = sections(names_bytes)
    entries = []
    for code, callback, threshold, low, high in (
            ('h06F', 'CJB', 25, [3, 5], [10, 50]),
            ('H0AZ', 'CJC', 30, [300, 400], [500, 4500])):
        if code not in names['h062']['Sellunits'].split(','):
            raise ValueError('Original initial shop does not sell ' + code)
        registration = f'call BC5(KF,${code.encode("ascii").hex().upper()},function {callback})'
        if registration not in source:
            raise ValueError('Original sold-unit callback changed')
        body = re.search(r'^function ' + callback + r' takes .*?^endfunction$', source,
                         re.M | re.S).group(0)
        for call in (f'if BFi(1,100)>{threshold} then',
                     f'set EK=BFi({high[0]},{high[1]})',
                     f'set EK=BFi({low[0]},{low[1]})'):
            if call not in body:
                raise ValueError('Original money payout changed: ' + call)
        row = balance[code]
        entries.append({
            'rawcode': code, 'name': names[code]['Name'], 'callback': callback,
            'callbackSha256': sha(body.encode('utf-8')),
            'goldCost': int(row['goldcost']),
            # Missing inherited fields stay unknown in source evidence.
            'lumberCostOverride': row.get('lumbercost'),
            'stockMaximum': int(row['stockMax']), 'stockRegenSeconds': int(row['stockRegen']),
            'stockStartOverride': row.get('stockStart'),
            'requiredResearch': names[code].get('Requires'),
            'branchRange': [1, 100], 'lowPayoutAtMost': threshold,
            'lowReturnRange': low, 'highReturnRange': high,
            'drawsPerPurchase': 2,
        })
    if 'set kN[JG]=kN[JG]+EK' not in source or 'if kN[JG]>=35000 then' not in source:
        raise ValueError('High-tier lifetime cap changed')
    return {
        'format': 'ordr-original-2323C-money-shop-v1', 'sourceW3xSha256': MAP_SHA,
        'sourceJassSha256': JASS_SHA, 'sourceUnitBalanceSha256': BALANCE_SHA,
        'sourceUnitStringsSha256': sha(names_bytes), 'initialShopRawcode': 'h062',
        'entries': entries, 'highTierGrossReturnGraduation': 35000,
        'retailParityVerified': False, 'nativeStockInitializationVerified': False,
        'limits': [
            'Costs/stock object overrides and payout callbacks verified from the original map.',
            'Missing base fields are not reported as observed zero overrides.',
            'Practice regular-money shop uses logical-time stock with an initially full shelf.',
            'Native stock initial amount/refill timing and the shared Warcraft RNG remain unverified.',
            'High-grade purchase requires R009 and graduation effects; it is not enabled.',
            'Money gambling is not unit gambling and must not increment unit-gambling missions.',
        ],
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--w3x', type=Path, required=True)
    parser.add_argument('--output', type=Path,
                        default=Path('data/original-2323C-money-gambling-shop.json'))
    args = parser.parse_args()
    args.output.write_text(json.dumps(extract(args.w3x), ensure_ascii=False, indent=2) + '\n',
                           encoding='utf-8')


if __name__ == '__main__':
    main()
