import re,sys,json,os
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')); import lom_paths
en={}
with open(lom_paths.TABLE,encoding='utf-8',errors='replace') as f:
    for line in f:
        if line.startswith('Story/'):
            k,_,v=line.partition(','); en[k[6:]]=v.strip().strip('"')
res=json.load(open('all_cond_switch.json',encoding='utf-8'))
CMP={0:'==',1:'!=',2:'<',3:'<=',4:'>',5:'>='}
def gname(v):
    if isinstance(v,dict): return v.get('name') or v.get('ref','').split(':')[-1] or '?'
    return str(v)
def cond(v):
    if isinstance(v,dict):
        if '_items' in v and '_logicOp' in v:
            op=' AND ' if v['_logicOp']==0 else ' OR '
            return '('+op.join(cond(i) for i in v['_items'])+')' if v['_items'] else '()'
        if '_compareOp' in v: return f"{gname(v['_value1'])} {CMP.get(v['_compareOp'])} {gname(v['_value2'])}"
        if '_condition' in v: return cond(v['_condition'])
    return str(v)
def defn(kind,name):
    v=res.get(f'{kind}:{name}')
    if not v: return '?'
    if kind=='ConditionResultData': return ' AND '.join(cond(i) for i in v.get('_items',[]))
    return ' | '.join(f"c{i+1}: {cond(it)}" for i,it in enumerate(v.get('_items',[])))
def summarize(name, maxlines=14):
    p=f'scripts/{name}.txt'
    if not os.path.exists(p): print('### MISSING',name); return
    t=open(p,encoding='utf-8',errors='replace').read(); lines=t.split('\n')
    print(f'### {name}  ({len(lines)} lines)')
    sw=[]
    for m in re.finditer(r'checkpointmanager\.(Switch|Condition|Dice)\("([^"]+)"',t):
        k={'Switch':'SwitchResultData','Condition':'ConditionResultData','Dice':'Dice'}[m.group(1)]
        s=f"  {m.group(1)} {m.group(2)}: {defn(k,m.group(2)) if k!='Dice' else '(dice)'}"
        if s not in sw: sw.append(s)
    for s in sw: print(s)
    opts=[]
    for m in re.finditer(r'"(O_[A-Za-z0-9_]+)"',t):
        o=m.group(1)
        if o not in opts: opts.append(o)
    if opts: print('  OPTIONS: '+' | '.join(f"{o}={en.get(o,'?')[:70]}" for o in opts))
    mods=[]
    for m in re.finditer(r'statmodifymanager\.(\w+)\(([^\n]*)\)',t):
        s=f"{m.group(1)}({m.group(2)[:60]})"
        if 'AddStory' in s: continue
        if s not in mods: mods.append(s)
    if mods: print('  MODS: '+' ; '.join(mods))
    nxt=[]
    for m in re.finditer(r'(?:SetNextScript|SetTempScript\(\d+,)\s*\(?"([^"]+)"',t):
        if m.group(1) not in nxt: nxt.append(m.group(1))
    if nxt: print('  NEXT: '+', '.join(nxt))
    other=re.findall(r'luamanager\.(NextRound|ChangeScene|Mission)\(([^)]*)\)',t)
    if other: print('  LUA: '+str(other[:6]))
    keys=[]
    for m in re.finditer(r'GetStoryText\("([^"]+)"\)',t):
        if m.group(1) not in keys: keys.append(m.group(1))
    print('  TEXT: '+' / '.join(f"[{k.split('_')[0]}] {en.get(k,'?')[:90]}" for k in keys[:maxlines]))
if __name__=='__main__':
    for n in sys.argv[1:]: summarize(n); print()
