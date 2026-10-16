"""Rebuild the current audit from the catalog and reviewed coverage decisions.

Run from any directory: python tools/update-rule-audit.py [--check]
"""
import json
from collections import Counter
from pathlib import Path
import sys

root = Path(__file__).resolve().parent.parent
catalog = json.loads((root / 'ForesTycoon.Rules/Catalog/current-game-rules.json').read_text(encoding='utf-8'))
coverage = json.loads((root / 'docs/rule-coverage.json').read_text(encoding='utf-8'))
entries = {}
for group in coverage['groups']:
    for key in group['ids']:
        if key in entries:
            raise ValueError('Duplicate coverage: ' + key)
        entries[key] = group
assert set(entries) == {r['id'] for r in catalog['rules']}, 'Catalog and coverage differ'
labels = dict(graph='Projektgráf', extension='Részleges felülírás', pending='Átvezetendő',
              engine='Motor', data='Típusadat', mixed='Motor és átvezetendő profil', presentation='Megjelenítés')
counts = Counter(x['status'] for x in entries.values())
lines = ['# Szabályrendszer: aktuális lefedettség', '',
         'Ez az aktuális lista; a korábbi, időközi hiánylistákat váltja fel. '
         'A `rule-coverage.json` felülvizsgált besorolását és a játék katalógusának kódhivatkozásait egyesíti. '
         'Újragenerálás: `python tools/update-rule-audit.py`; eltérésellenőrzés: `--check`.', '',
         '**A darabszám nem készültségi százalék.** Egy tétel több gráfot és több motorprimitívet is takarhat. '
         'A teszthivatkozás az adott terület ellenőrzési helye; nem állítja, hogy a még hiányzó gráfos átvezetés kész.', '',
         '; '.join(f'{labels[k]}: **{v}**' for k, v in sorted(counts.items())) + '.', '',
         '## Tételes leltár', '',
         '| Folyamat | Besorolás | Forrás | Szerkesztés | Határ / hátralévő munka | Ellenőrzés |',
         '| --- | --- | --- | --- | --- | --- |']
for rule in catalog['rules']:
    entry = entries[rule['id']]
    sources = '<br>'.join(f'[{s["file"]}](../{s["file"]}) · `{s["symbol"]}`' for s in rule['sources'])
    tests = '<br>'.join(f'[{Path(t).stem}](../{t})' for t in entry['tests'])
    lines.append(f'| `{rule["id"]}` — {rule["name"]} | {labels[entry["status"]]} | {sources} | {entry["editor"]} | {entry["remaining"]} | {tests} |')
lines += ['', '## A katalóguson kívül követett területek', '']
for entry in coverage['outsideCatalog']:
    lines += [f'- **{entry["id"]} — {labels[entry["status"]]}:** {entry["remaining"]} '
              f'Forrás: [{entry["symbol"]}](../{entry["source"]}). Szerkesztés: {entry["editor"]}. '
              + 'Ellenőrzés: ' + ', '.join(f'[{Path(t).stem}](../{t})' for t in entry['tests']) + '.']
lines += ['', '## Közös elfogadási feltételek', '',
          '- Egy tárolt szabálydefiníciót használ az editor, az előnézet és a játék. A kiváltott natív alapképlet törlendő.',
          '- Angol stabil mezőazonosítók; kijelzett objektum- és mezőnév, mértékegység, értelmezhető kikapcsolás.',
          '- Minden növekvő munka előkészítése és lezárása is szeletelt. Szinkron adapter nem hívható munkaszeletből.',
          '- Mentés teljes tick/parancscsomag/pályapublikálás határán; nincs azonnali, korlátlan sorürítés.',
          '- Eltérő szeletméret, közben érkező parancs, projektcsere, mentés/betöltés és naplós visszajátszás azonos állapotot ad.',
          '- Nagypályás mérés: p95/p99/max munkaszelet és képkockakeret, logikai tickköltség és munkahátralék. A mérési idő nem vezérelheti a szimulációs döntéseket.', '',
          'A motor működése és a mentési határ: [engine-architecture.md](engine-architecture.md). '
          'A közös felület: [rule-editor.md](rule-editor.md). '
          'A legutóbbi ellenőrzési és mérési eredményeket külön a lezárási jegyzőkönyv rögzíti.', '']
result = '\n'.join(lines)
target = root / 'docs/rule-runtime-audit.md'
if '--check' in sys.argv:
    if target.read_text(encoding='utf-8') != result:
        raise SystemExit('Audit is stale. Run python tools/update-rule-audit.py')
else:
    target.write_text(result, encoding='utf-8')
print(f'{len(entries)} catalog items, {len(coverage["outsideCatalog"])} additional areas: covered')
