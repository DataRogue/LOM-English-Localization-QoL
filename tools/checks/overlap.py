import csv, glob, os, sys, io, re, collections
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')); import lom_paths
csv.field_size_limit(10**9)
SD=lom_paths.STRINGS_DIR
def parse_simple(text):
    # mirror StringOverrides.ParseCsv
    rows=[];row=[];sb=[];q=False;i=0
    while i<len(text):
        c=text[i]
        if q:
            if c=='"':
                if i+1<len(text) and text[i+1]=='"': sb.append('"'); i+=1
                else: q=False
            else: sb.append(c)
        else:
            if c=='"': q=True
            elif c==',': row.append(''.join(sb)); sb=[]
            elif c=='\n':
                row.append(''.join(sb)); sb=[]
                if len(row)>1 or row[0]: rows.append(row)
                row=[]
            elif c=='\r': pass
            else: sb.append(c)
        i+=1
    if sb or row:
        row.append(''.join(sb)); rows.append(row)
    if rows and rows[0] and rows[0][0].startswith('﻿'): rows[0][0]=rows[0][0].lstrip('﻿')
    return rows
ov={}; perfile=collections.OrderedDict()
for f in sorted(glob.glob(os.path.join(SD,'*.csv')), key=str.lower):
    n=0; keys=[]
    for r in parse_simple(open(f,encoding='utf-8-sig').read()):
        if len(r)>=2:
            k=r[0].strip()
            if k and not k.startswith('#') and k!='key':
                ov[k]=r[1].replace('\n','\n'); n+=1; keys.append(k)
    perfile[os.path.basename(f)]=keys
def load_table(p):
    m={}
    with open(p,encoding='utf-8-sig',newline='') as fh:
        for r in csv.reader(fh):
            if len(r)>=2 and r[0] and r[0] not in m: m[r[0]]=r[1]
    return m
rev=load_table(lom_paths.TABLE)
orig=load_table(lom_paths.BASE_REF_TABLE)   # the OverLlm release (0.4: lom_paths.ORIG_TABLE)
print('override keys',len(ov),'table rev',len(rev),'orig',len(orig))
inrev=[k for k in ov if k in rev]; notrev=[k for k in ov if k not in rev]
same=[k for k in inrev if rev[k]==ov[k]]
print('overrides also in revised table',len(inrev),'identical value',len(same),'differ',len(inrev)-len(same))
print('overrides NOT in revised table',len(notrev))
print('overrides in original table',sum(1 for k in ov if k in orig))
for fn,keys in perfile.items():
    a=sum(1 for k in keys if k in rev); s=sum(1 for k in keys if k in rev and rev[k]==ov[k])
    print(f"  {fn}: {len(keys)} rows, {a} in table ({s} identical), {len(keys)-a} not in table")
pref=collections.Counter(k.split('/')[0] for k in notrev)
print('not-in-table prefixes',pref.most_common(20))
print('examples not in table',notrev[:25])
# key prefix breakdown of override keys
print('override prefixes',collections.Counter(k.split('/')[0] for k in ov).most_common(30))
# how many revised-table values contain CJK
cjk=re.compile('[一-鿿]')
print('revised table values with CJK',sum(1 for v in rev.values() if cjk.search(v)))
print('table prefixes',collections.Counter(k.split('/')[0] for k in rev).most_common(40))
# dup keys in overrides across files
seen={};dups=0
for fn,keys in perfile.items():
    for k in keys:
        if k in seen: dups+=1
        seen[k]=fn
print('duplicate override keys across files',dups)
print([k for k in notrev if not k.startswith('DiceHeader')])
# which files hold the 401 identical
print(collections.Counter(k.split('/')[0] for k in same).most_common(10))
