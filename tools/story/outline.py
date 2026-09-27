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
    if not v: return ''
    if kind=='ConditionResultData': return ' AND '.join(cond(i) for i in v.get('_items',[]))
    return ' | '.join(f"c{i+1}:{cond(it)}" for i,it in enumerate(v.get('_items',[])))
def outline(name, textlimit=1):
    p=f'scripts/{name}.txt'
    if not os.path.exists(p): print('### MISSING',name); return
    lines=open(p,encoding='utf-8',errors='replace').read().split('\n')
    print(f'### {name}')
    vars={}; optvars={}
    for l in lines:
        m=re.search(r'local (\w+) = checkpointmanager\.(Switch|Condition|Dice)\("([^"]+)"',l)
        if m:
            kind={'Switch':'SwitchResultData','Condition':'ConditionResultData','Dice':'Dice'}[m.group(2)]
            vars[m.group(1)]=f"{m.group(2)}:{m.group(3)}[{defn(kind,m.group(3)) if kind!='Dice' else 'dice'}]"
    # option arrays: optionX[i] = "O_key"
    for l in lines:
        m=re.search(r'(\w+)\[(\d+)\] = "(O_[^"]+)"',l)
        if m: optvars.setdefault(m.group(1),{})[m.group(2)]=m.group(3)
        m=re.search(r'local (choice_\w+) = choose\((\w+)\)',l)
        if m: optvars[m.group(1)]=optvars.get(m.group(2),{})
    shown=0; last_block=None
    for i,l in enumerate(lines):
        s=l.strip(); ind=(len(l)-len(l.lstrip('\t')))
        pad='  '*ind
        m=re.match(r'(if|elseif) (\w+) == (\d+) then',s)
        if m:
            v=m.group(2); n=m.group(3)
            if v in vars: print(f"{pad}{m.group(1)} {vars[v]} == {n}")
            elif v in optvars: print(f"{pad}{m.group(1)} CHOICE {optvars[v].get(n,'?')} = {en.get(optvars[v].get(n,''),'?')[:60]}")
            else: print(f"{pad}{m.group(1)} {v} == {n}")
            shown=0; continue
        m=re.search(r'statmodifymanager\.(Player|Character|SetFlag|AddFlag|Mission|AddBook|AddMisc|AddSpecial|SetPlayer)\((.*)\)',s)
        if m:
            args=m.group(2)
            args=re.sub(r', "", [01]\)?$','',args)
            if 'AddStory' not in s: print(f"{pad}  -> {m.group(1)}({args[:50]})")
            continue
        m=re.search(r'ChangeScene\("(\w+)", "([^"]*)"',s)
        if m: print(f"{pad}  -> SCENE {m.group(1)} {m.group(2)}"); continue
        m=re.search(r'SetNextScript\("([^"]+)"\)',s)
        if m: print(f"{pad}  -> NEXT {m.group(1)}"); continue
        m=re.search(r'GetStoryText\("([^"]+)"\)',s)
        if m and shown<textlimit:
            print(f"{pad}  \"{en.get(m.group(1),m.group(1))[:100]}\""); shown+=1
if __name__=='__main__':
    for n in sys.argv[1:]: outline(n); print()
