// Source projection only. Does not run Unity, NUnit or combat simulation.
// node tools/build_player_upgrade_runtime.mjs UnitBalance.slk UpgradeData.slk war3map.j
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const inputs = process.argv.slice(2);
if (inputs.length !== 3) throw Error('Expected UnitBalance.slk UpgradeData.slk war3map.j');
const bytes = inputs.map(p => fs.readFileSync(p));
const hash = b => crypto.createHash('sha256').update(b).digest('hex');
const expected = [
  'f7f26217d8097b9791723feb02d9181277d9afb1947dc0a3bf553ce590ab3af5',
  '0343cc53ccb5be4e54005d93bee867f55536abe07e3afa329f35cf2408347b13',
  '96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d'
];
bytes.forEach((b, i) => { if (hash(b) !== expected[i]) throw Error('Wrong pinned source: ' + inputs[i]); });
function slk(text) {
  const rows = new Map(); let x = 1, y = 1;
  for (const line of text.split(/\r?\n/)) {
    if (!line.startsWith('C;')) continue;
    const a = line.match(/(?:^|;)X(\d+)/), b = line.match(/(?:^|;)Y(\d+)/), k = line.match(/(?:^|;)K(.*)$/);
    if (a) x = +a[1]; if (b) y = +b[1];
    if (!k) continue;
    if (!rows.has(y)) rows.set(y, {});
    rows.get(y)[x] = k[1].startsWith('"') ? k[1].slice(1, -1).replaceAll('""', '"') : +k[1];
  }
  const header = rows.get(1);
  return [...rows].filter(([row]) => row !== 1).map(([, row]) =>
    Object.fromEntries(Object.entries(row).map(([column, value]) => [header[column], value])));
}
const balance = slk(bytes[0].toString('utf8')), upgrades = slk(bytes[1].toString('utf8'));
const jass = bytes[2].toString('utf8');
const body = name => {
  const start = jass.indexOf('function ' + name + ' ');
  if (start < 0) throw Error('Missing source function ' + name);
  return jass.slice(start, jass.indexOf('endfunction', start));
};
const rawcode = hex => Buffer.from(hex, 'hex').toString('ascii');
const initial = [...body('CUo').matchAll(/SetPlayerTechResearchedSwap\(\$([A-F0-9]+),(\d+),Player\(EK\)\)/g)]
  .map(m => [rawcode(m[1]), +m[2]]);
const links = {};
for (const m of body('CV8').matchAll(/if GetResearched\(\)==\$([A-F0-9]+) then([\s\S]*?)endif/g)) {
  const values = [...m[2].matchAll(/SetPlayerTechResearched\(Cl,\$([A-F0-9]+),GetPlayerTechCount\(Cl,\$([A-F0-9]+),true\)\+1\)/g)]
    .map(v => ({ id: rawcode(v[1]), from: rawcode(v[2]) }));
  if (values.length) links[rawcode(m[1])] = values;
}
const evidence = JSON.parse(fs.readFileSync(path.join(root, 'data/original-2323C-player-research-upgrades.json'), 'utf8'));
const shop = [...evidence.shops.h06Q, ...evidence.shops.h06R];
const ids = new Set([...shop.map(r => r.id), ...Object.values(links).flat().map(r => r.id)]);
const output = {
  format: 'ordr-source-player-upgrades-v1', sourceW3xSha256: evidence.source.sha256,
  sourceJassSha256: expected[2],
  sourceFiles: { 'Units/UnitBalance.slk': expected[0], 'Units/UpgradeData.slk': expected[1] },
  runtimeParityVerified: false,
  modelNotes: [
    'Sparse SLK lumber/time fields remain absent in evidence; standalone source model uses no extra debit/wait, not a native lifecycle approval.',
    'CUo initial tech levels and CV8 research-start increments are modeled once; no speculative second native completion increment.',
    'Requires12/R00H modeled at next level 12 and later, unlocked by CIN level-20 Crocodile o01W defeat; native level-index semantics unverified.'
  ],
  shopIds: shop.map(r => r.id), initialLevels: Object.fromEntries(initial), linkedTechUpdates: links,
  upgrades: upgrades.filter(r => ids.has(r.upgradeid)).map(r => ({
    id: r.upgradeid, maxLevel: r.maxlevel, goldBase: r.goldbase, goldMod: r.goldmod ?? 0,
    attackBase: r.effect2 === 'ratx' ? r.base2 : 0, attackMod: r.effect2 === 'ratx' ? r.mod2 : 0,
    speedBase: r.effect1 === 'rats' ? r.base1 : 0, speedMod: r.effect1 === 'rats' ? r.mod1 : 0,
    requiresBossFromLevel: shop.find(s => s.id === r.upgradeid)?.requires_12 ? 12 : 0
  })),
  unitUpgrades: Object.fromEntries(balance.filter(r => typeof r.upgrades === 'string' &&
    r.upgrades.split(',').some(id => ids.has(id))).map(r =>
    [r.unitBalanceID, r.upgrades.split(',').filter(id => ids.has(id))]))
};
if (shop.length !== 18 || output.upgrades.length !== 38 || Object.keys(output.unitUpgrades).length !== 355)
  throw Error('Pinned upgrade source count mismatch');
fs.writeFileSync(path.join(root, 'data/original-2323C-player-upgrade-runtime.json'), JSON.stringify(output, null, 2) + '\n');
console.log('Projected 18 shop upgrades, 20 linked technologies, 355 object upgrade lists. Runtime parity remains unverified.');
