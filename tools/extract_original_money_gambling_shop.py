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
    bodies = {name: re.search(r'^function ' + name + r' takes .*?^endfunction$',
                              source, re.M | re.S).group(0)
              for name in ('CIK', 'CIJ', 'CJC', 'CI8', 'CI7', 'CJF', 'CJE')}
    rare = [bytes.fromhex(code).decode('latin-1') for code in
            re.findall(r'UnitPoolAddUnitType\(kQ,\$([0-9A-Fa-f]{8}),1\)', bodies['CJE'])]
    special = [bytes.fromhex(code).decode('latin-1') for code in
               re.findall(r'UnitPoolAddUnitType\(kR,\$([0-9A-Fa-f]{8}),1\)', bodies['CJE'])]
    graduated_offers = {
        'traitPoint': {
            'rawcode': 'h0AL', 'name': names['h0AL']['Name'],
            'callback': 'CI7', 'goldCost': int(balance['h0AL']['goldcost']),
            'pointAmount': 1, 'stockMaximum': int(balance['h0AL']['stockMax']),
            'stockRegenSeconds': int(balance['h0AL']['stockRegen']),
            'sharedOriginalResource': 'PLAYER_STATE_RESOURCE_FOOD_USED',
        },
        'rareUnit': {
            'rawcode': 'H0B0', 'name': names['H0B0']['Name'],
            'callback': 'CJF', 'goldCost': int(balance['H0B0']['goldcost']),
            'requiredResearch': names['H0B0']['Requires'], 'purchaseLimit': 1,
            'stockMaximum': int(balance['H0B0']['stockMax']),
            'stockRegenSeconds': int(balance['H0B0']['stockRegen']),
            'rareBranchPercent': 99, 'rarePool': rare,
            'specialBranchPercent': 1, 'specialPool': special,
            'rareBonusAbility': 'A0YE', 'rareBonusExcept': 'h05X',
        },
    }
    if (graduated_offers['traitPoint']['goldCost'] != 15000 or
            graduated_offers['rareUnit']['goldCost'] != 5000 or
            graduated_offers['traitPoint']['stockMaximum'] != 1 or
            graduated_offers['rareUnit']['stockMaximum'] != 1 or
            graduated_offers['traitPoint']['stockRegenSeconds'] != 3600 or
            graduated_offers['rareUnit']['stockRegenSeconds'] != 3600 or
            graduated_offers['rareUnit']['requiredResearch'] != 'R01N' or
            len(rare) != 43 or len(special) != 7 or
            'h05X' not in rare or 'h0BG' in rare or
            'call SetPlayerState(Cl,PLAYER_STATE_RESOURCE_FOOD_USED,' not in bodies['CI7'] or
            'call BC5(KF,$6830414C,function CI7)' not in source or
            'call BC5(KF,$48304230,function CJF)' not in source or
            'BFi(1,100)<=99' not in bodies['CJF'] or
            'PlaceRandomUnit(kQ' not in bodies['CJF'] or
            'PlaceRandomUnit(kR' not in bodies['CJF']):
        raise ValueError('Graduated h0AL/H0B0 sale or CJE/CJF pool changed')
    if ('call BDB($6F303156,function CIK)' not in source or
            'call CIJ(JG)' not in bodies['CIK'] or
            'call SetPlayerTechResearched(Player(JG),$52303039,1)' not in bodies['CIJ'] or
            'set i5[JG]=i5[JG]+5' not in bodies['CJC'] or
            'set i6[JG]=CreateUnit(Player(JG),$68303843,EN,JS,bj_UNIT_FACING)' not in bodies['CJC'] or
            'call SetPlayerTechResearched(Player(JG),$5230314D,1)' not in bodies['CJC'] or
            'PLAYER_STATE_RESOURCE_LUMBER)+1)' not in bodies['CI8'] or
            int(balance['h0AK']['goldcost']) != 10000 or
            names['h08C']['Sellunits'] != 'h0AL,H0B0'):
        raise ValueError('Money shop unlock/graduation source changed')
    return {
        'format': 'ordr-original-2323C-money-shop-v1', 'sourceW3xSha256': MAP_SHA,
        'sourceJassSha256': JASS_SHA, 'sourceUnitBalanceSha256': BALANCE_SHA,
        'sourceUnitStringsSha256': sha(names_bytes), 'initialShopRawcode': 'h062',
        'entries': entries, 'highTierGrossReturnGraduation': 35000,
        'highTierUnlock': {'bossRawcode': 'o01V', 'bossName': '아론',
                          'deathCallback': 'CIK', 'researchCallback': 'CIJ',
                          'research': 'R009'},
        'graduation': {'replacementShop': 'h08C', 'research': 'R01M',
                       'moneyOffersRemoved': ['h06F', 'H0AZ'],
                       'addedWoodPurchaseRights': 5, 'woodPurchaseRawcode': 'h0AK',
                       'woodPurchaseGoldCost': 10000, 'woodPerPurchase': 1,
                       'additionalShopOffers': ['h0AL', 'H0B0'],
                       'verifiedOffers': graduated_offers},
        'progressionCallbackHashes': {name: sha(body.encode('utf-8'))
                                     for name, body in bodies.items()},
        'retailParityVerified': False, 'nativeStockInitializationVerified': False,
        'limits': [
            'Costs/stock object overrides and payout callbacks verified from the original map.',
            'Missing base fields are not reported as observed zero overrides.',
            'Practice regular/high money shops use logical-time stock with initially full shelves.',
            'Native stock initial amount/refill timing and the shared Warcraft RNG remain unverified.',
            'Practice high-grade unlock follows an actual modeled o01V boss death; no free R009 unlock.',
            'Graduation stops both money offers, records R01M and five paid wood-purchase rights.',
            'Graduated h0AL trait points credit the shared wallet; H0B0 CJE/CJF provides one rare/special actor after R01N.',
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
                           encoding='utf-8', newline='\n')


if __name__ == '__main__':
    main()
