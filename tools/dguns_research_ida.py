"""Read-only function evidence, run against a COPY of the retained IDA DB."""
from pathlib import Path
import json
import ida_auto, ida_funcs, ida_hexrays, ida_lines, ida_name, idautils, idc

OUT=Path('E:/projects/SurvivalcraftMechDefense/local/dguns/unit-library/native-analysis')
OUT.mkdir(parents=True,exist_ok=True)
ida_auto.auto_wait(); ida_hexrays.init_hexrays_plugin()
targets={}; refs={}
globals_=['enepara','plbody','atkpr','partsdt','kind2str']
for name in globals_:
    ea=ida_name.get_name_ea(idc.BADADDR,name)
    rs=[]
    if ea!=idc.BADADDR:
        for ref in idautils.XrefsTo(ea):
            fn=ida_funcs.get_func(ref.frm)
            if fn:
                targets[fn.start_ea]=ida_name.get_name(fn.start_ea)
                rs.append(dict(fromAddress=hex(ref.frm),function=hex(fn.start_ea)))
    refs[name]=dict(address=hex(ea),xrefs=rs)
for ea,name in idautils.Names():
    if any(s in name for s in ['get_ene_para','get_atk_param','make_body_name','set_king_boss','shdRegistMotion','prg_RD','prg_MON0','pw_ini_anm','pw_ini_actanm','pw_aloc','echPchrDispSet','set_corp']):
        fn=ida_funcs.get_func(ea)
        if fn and not name.startswith('.'):
            targets[fn.start_ea]=name
            if 'get_ene_para' in name:
                for ref in idautils.XrefsTo(ea):
                    caller=ida_funcs.get_func(ref.frm)
                    if caller:
                        targets[caller.start_ea]=ida_name.get_name(caller.start_ea)
                        if ida_name.get_name(caller.start_ea).startswith('.'):
                            for r2 in idautils.XrefsTo(caller.start_ea):
                                c2=ida_funcs.get_func(r2.frm)
                                if c2: targets[c2.start_ea]=ida_name.get_name(c2.start_ea)
records=[]
for ea,name in sorted(targets.items()):
    row=dict(address=hex(ea),name=name)
    try:
        (OUT/(hex(ea)+'.c')).write_text(str(ida_hexrays.decompile(ea)),encoding='utf-8')
        row['decompiled']=True
    except Exception as ex: row['error']=str(ex)
    (OUT/(hex(ea)+'.asm')).write_text('\n'.join(f'{p:x}: {ida_lines.tag_remove(idc.generate_disasm_line(p,0))}' for p in idautils.FuncItems(ea)),encoding='utf-8')
    records.append(row)
# This exact-build program table was identified by retained kindno_to_chrprg.
programs=[]
for i in range(27):
    ea=idc.get_qword(0x12c408+i*8)
    programs.append(dict(index=i,address=hex(ea),name=ida_name.get_name(ea)))
(OUT/'index.json').write_text(json.dumps(dict(functions=records,globals=refs,programs=programs),indent=2),encoding='utf-8')
idc.qexit(0)
