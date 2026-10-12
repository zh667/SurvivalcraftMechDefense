"""Read-only SP-alpha native evidence; new database under ignored local/."""
from pathlib import Path
import json
import ida_auto, ida_funcs, ida_hexrays, ida_lines, idautils, idc

OUT = Path('E:/projects/SurvivalcraftMechDefense/local/dguns-spalpha/unit-library/native-analysis')
OUT.mkdir(parents=True, exist_ok=True)
ida_auto.auto_wait()
ida_hexrays.init_hexrays_plugin()
terms = ('onm_datbin', 'load_pchr', 'cal_ply_stat', 'set_1corp', 'get_ene_para',
         'set_player', 'kindno_to_chrprg', 'echPchrDispSet', 'make_body_name',
         'shdPchr', 'shdLoad', 'get_atk_param', 'load_file', 'set_ply_model',
         'make_body', 'body_name', 'get_mes', 'get_str', 'set_menustr_bodysel',
         'draw_robo_stat', 'pw_aloc')
rows = []
for ea, name in idautils.Names():
    if name.startswith('.') or not any(t in name for t in terms): continue
    fn = ida_funcs.get_func(ea)
    if not fn or fn.start_ea != ea: continue
    row = dict(address=hex(ea), name=name)
    try:
        (OUT/(hex(ea)+'.c')).write_text(str(ida_hexrays.decompile(ea)), encoding='utf-8')
        row['decompiled'] = True
    except Exception as exc: row['error'] = str(exc)
    (OUT/(hex(ea)+'.asm')).write_text('\n'.join(f'{p:x}: {ida_lines.tag_remove(idc.generate_disasm_line(p,0))}' for p in idautils.FuncItems(ea)), encoding='utf-8')
    rows.append(row)
(OUT/'index.json').write_text(json.dumps(rows, indent=2), encoding='utf-8')
idc.qexit(0)
