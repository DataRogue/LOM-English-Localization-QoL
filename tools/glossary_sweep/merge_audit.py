"""Glossary sweep, step 3: fold the second-opinion audit into the sweep's verdicts.

A fix is accepted only when the sweep's refuter accepted it (verify:N in the sweep journal) and the audit kept it (audit:M
in the audit journal; a fix the audit has no verdict for is not accepted). Writes a journal with one verify:N result per
sweep chunk; apply_fullpass.py takes the later of two results with the same label, so pass it after the sweep journal:

  python merge_audit.py <sweep journal.jsonl> <audit journal.jsonl> <out.jsonl>
  python ../fullpass/apply_fullpass.py <sweep journal.jsonl> <out.jsonl> --chunks LOM_Localization/fullpass/glossary --pass glossary-2026-09
"""
import sys, json, collections
sys.stdout.reconfigure(encoding='utf-8')


def results(path):
    label_of, out = {}, {}
    for line in open(path, encoding='utf-8'):
        try:
            e = json.loads(line)
        except ValueError:
            continue
        if e.get('type') == 'started' and e.get('agentId'):
            label_of[e['agentId']] = e.get('label', '')
        elif e.get('type') == 'result' and e.get('agentId'):
            r = e.get('result')
            if isinstance(r, str):
                try:
                    r = json.loads(r)
                except ValueError:
                    r = None
            lab = label_of.get(e['agentId'], '').replace(':retry', '')
            if lab and isinstance(r, dict):
                out[lab] = r
    return out


sweep, audit, dest = sys.argv[1:4]
sw = results(sweep)
keep = {}
for lab, r in results(audit).items():
    if lab.startswith('audit:'):
        for v in r.get('verdicts') or []:
            if v.get('id'):
                keep[v['id']] = (bool(v.get('keep')), v.get('reason', ''))
stats = collections.Counter()
with open(dest, 'w', encoding='utf-8', newline='\n') as f:
    for lab, r in sorted(sw.items()):
        if not lab.startswith('verify:'):
            continue
        verdicts = []
        for v in r.get('verdicts') or []:
            i = v.get('id')
            k, why = keep.get(i, (False, 'no audit verdict'))
            acc = bool(v.get('accept')) and k
            stats['refuter accepted' if v.get('accept') else 'refuter rejected'] += 1
            if v.get('accept'):
                stats['audit kept' if k else ('audit reverted' if i in keep else 'no audit verdict')] += 1
            verdicts.append({'id': i, 'accept': acc, 'reason': v.get('reason', '') if not v.get('accept') else ('' if acc else 'audit: ' + why)})
        aid = 'merge-' + lab.replace(':', '-')
        f.write(json.dumps({'type': 'started', 'agentId': aid, 'label': lab}, ensure_ascii=False) + '\n')
        f.write(json.dumps({'type': 'result', 'agentId': aid, 'result': {'verdicts': verdicts}}, ensure_ascii=False) + '\n')
print(dict(stats), '->', dest)
