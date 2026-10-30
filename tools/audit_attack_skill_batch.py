#!/usr/bin/env python3
"""Audit twenty attack callbacks against the pinned, unmodified original JASS.

This produces implementation inputs, never runtime authorization. Follow the
whole callback (including summoned objects and indirect triggers), not just its
damage literals. Original source files remain outside the repository.
"""
import argparse
import hashlib
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
JASS_SHA = '96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d'
TARGETS = ('U-04', 'U-13', 'S-03', 'S-13', 'R-11', 'R-12', 'R-13',
           'R-21', 'R-23', 'R-26', 'R-30', 'R-31', 'R-35', 'R-38',
           'H-11', 'H-16', 'H-18', 'L-29', 'L-33', 'RN-04')


def code_only(text):
    # Preserve line numbers while excluding comments AND string literals, so
    # a tooltip containing a call cannot grant a unit a callback or a dependency.
    return re.sub(r'"(?:\\.|[^"\\])*"|//[^\r\n]*',
                  lambda m: ' ' * len(m.group()), text)


def functions(source):
    clean = code_only(source)
    result = {}
    for m in re.finditer(r'(?m)^function (\w+) takes[^\n]*\n', clean):
        end = re.search(r'(?m)^endfunction\b', clean[m.end():])
        if end is None:
            raise ValueError('Unterminated function: ' + m[1])
        stop = m.end() + end.end()
        if m[1] in result:
            raise ValueError('Duplicate function: ' + m[1])
        body = source[m.start():stop]
        result[m[1]] = (body, clean[m.start():stop], source.count('\n', 0, m.start()) + 1)
    return result


def inspect_function(name, table):
    body, clean, line = table[name]
    refs = set(re.findall(r'\b(?:call\s+|function\s+)(\w+)', clean))
    # Calls inside expressions (RNG, predicates, getters) are dependencies too.
    refs.update(re.findall(r'\b(\w+)\s*\(', clean))
    refs.discard(name)
    return {
        'function': name, 'line': line,
        'sha256': hashlib.sha256(body.encode()).hexdigest(),
        'customDependencies': sorted(refs & table.keys()),
        'externalDependencies': sorted(refs - table.keys()),
        'sourceOperations': [x.strip() for x in body.splitlines()
                             if re.search(r'\b(?:BFi|BFj|Bcb|Bcc|UnitDamage\w*|'
                                          r'CreateUnit\w*|Issue\w*Order\w*|UnitAddAbility|'
                                          r'UnitApplyTimedLife|TriggerExecute|BiZ|Bd[258])\s*\(',
                                          code_only(x))],
    }


def build(path, base):
    raw = path.read_bytes()
    if hashlib.sha256(raw).hexdigest() != JASS_SHA:
        raise ValueError('Original JASS SHA mismatch')
    source = raw.decode('utf-8-sig').replace('\r\n', '\n')
    table = functions(source)
    clean = code_only(source)
    registrations = {}
    for m in re.finditer(r'call w5\(\$([0-9A-Fa-f]{8}),function (\w+)\)', clean):
        rawcode = bytes.fromhex(m[1]).decode('ascii')
        registrations.setdefault(rawcode, set()).add(m[2])
    units = {x['catalogId']: x for x in base['catalogEntries']}
    records = []
    for uid in TARGETS:
        unit = units[uid]
        callbacks = sorted(registrations.get(unit['originalRawcode'], ()))
        if len(callbacks) != 1:
            raise ValueError('Expected one audited attack callback: ' + uid)
        record = inspect_function(callbacks[0], table)
        # Capture one direct layer, without pretending it is complete closure.
        dependencies = [inspect_function(n, table) for n in record['customDependencies']]
        records.append({'catalogId': uid, 'name': unit['catalogName'],
                        'rawcode': unit['originalRawcode'], 'callback': record,
                        'directCustomDependencies': dependencies,
                        'runtimeConnectedByThisAudit': False,
                        'remaining': ['공격 선언 시점 연결', '전체 조건·난수 순서 재현',
                                      '보조 객체·주문 및 후속 함수 구현',
                                      '피해·상태·처치·보상 통합 검증']})
    return {'format': 'ordr-attack-skill-batch-audit-v1', 'sourceJassSha256': JASS_SHA,
            'unitCount': len(records), 'runtimeAuthorization': False,
            'scope': '20 source callback identities and direct dependencies; not executable skills',
            'dispatch': [inspect_function(n, table) for n in ('w5', 'w6', 'w7')],
            'units': records}


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--jass', type=Path, required=True)
    p.add_argument('--verify', action='store_true')
    args = p.parse_args()
    result = build(args.jass, json.loads((ROOT / 'data/original-2323C-map-base-stats.json').read_text()))
    output = ROOT / 'data/original-2323C-attack-skill-batch.json'
    encoded = json.dumps(result, ensure_ascii=False, indent=2) + '\n'
    if args.verify:
        if output.read_text() != encoded:
            raise SystemExit('Attack skill audit is stale')
    else:
        output.write_text(encoded)
    print('Audited 20 source callbacks; runtime authorization remains false')

if __name__ == '__main__':
    main()
