import re,sys,json,os
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')); import lom_paths
en={}
with open(lom_paths.TABLE,encoding='utf-8',errors='replace') as f:
    for line in f:
        if line.startswith('Story/'):
            k,_,v=line.partition(','); en[k[6:]]=v.strip().strip('"')
def branch(name,switch,ntext=2):
    lines=open(f'scripts/{name}.txt',encoding='utf-8',errors='replace').read().split('\n')
    var=None
    for l in lines:
        m=re.search(r'local (\w+) = checkpointmanager\.(?:Switch|Condition)\("'+re.escape(switch)+r'"\)',l)
        if m: var=m.group(1); break
    if not var:
        # Condition used in talk menus: talk[n] = X and "..." or "~x"
        for l in lines:
            if f'local {switch} = checkpointmanager.Condition("{switch}")' in l: var=switch
        if not var: print(f'  {name}: {switch} not found'); return
        for l in lines:
            m=re.search(r'\w+\[(\d+)\] = '+var+r' and "([^"]+)"',l)
            if m:
                key=m.group(2).split('+')[1] if '+' in m.group(2) else m.group(2)
                print(f'  {name}: {switch} unlocks menu option {key} = {en.get(key,"?")[:80]}'); return
    for case in (1,2,3):
        for j,l in enumerate(lines):
            if re.search(r'\b(if|elseif) '+var+r' == '+str(case)+r' then',l):
                ind=len(l)-len(l.lstrip('\t')); out=[]; mods=[]; nxt=[]
                for k in range(j+1,min(j+400,len(lines))):
                    lk=lines[k]; li=len(lk)-len(lk.lstrip('\t'))
                    if li<=ind and re.match(r'\s*(elseif|end|else)\b',lk): break
                    m=re.search(r'GetStoryText\("([^"]+)"\)',lk)
                    if m and len(out)<ntext: out.append(en.get(m.group(1),m.group(1))[:130])
                    m=re.search(r'statmodifymanager\.(Player|Character|SetFlag|AddFlag|Mission|AddBook|AddSpecial|AddMisc)\(([^)]*)\)',lk)
                    if m: mods.append(m.group(1)+'('+re.sub(r', "", [01]$','',m.group(2))[:36]+')')
                    m=re.search(r'SetNextScript\("([^"]+)"\)|ChangeScene\("(\w+)", "([^"]*)"',lk)
                    if m: nxt.append(m.group(1) or (m.group(2)+':'+m.group(3)))
                print(f'  {name}: {switch} case{case}: '+' / '.join(out)+((' || mods: '+'; '.join(mods[:8])) if mods else '')+((' || next: '+','.join(nxt[:3])) if nxt else ''))
                break
if __name__=='__main__':
    for arg in sys.argv[1:]:
        n,s=arg.split(':'); branch(n,s)
