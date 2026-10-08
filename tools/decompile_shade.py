"""Run with IDAPython against the retained arm64 library copy, never the APK."""
import json
from pathlib import Path
import ida_auto
import ida_funcs
import ida_hexrays
import ida_lines
import ida_name
import idautils
import idc

ida_auto.auto_wait()
out = Path(idc.get_input_file_path()).parent / 'native'
out.mkdir(exist_ok=True)
need = ['shdRegistPchr', 'adMakeChrPacket', 'shdRegistMotion', 'shdPdispInit',
        'shdCalPchrMat', 'shdPchrCalAllMat', 'shdPAnmExec', 'load_pchr', 'shdPanmCalAnmAdr',
        'sub_5EF3C', 'sub_5F1FC', 'regist_kind', 'shdCalOrgMat']
ida_hexrays.init_hexrays_plugin()
records = []
for ea in idautils.Functions():
    name = ida_name.get_name(ea)
    if not any(s in name for s in need) or not ida_funcs.get_func(ea):
        continue
    record = dict(name=name, address=hex(ea))
    try:
        c = ida_hexrays.decompile(ea)
        (out/(hex(ea)+'.c')).write_text(str(c), encoding='utf-8')
        record['decompiled'] = True
    except Exception as exc:
        record['error'] = str(exc)
    asm = [f'{p:x}: {ida_lines.tag_remove(idc.generate_disasm_line(p,0))}' for p in idautils.FuncItems(ea)]
    (out/(hex(ea)+'.asm')).write_text('\n'.join(asm), encoding='utf-8')
    records.append(record)
(out/'functions.json').write_text(json.dumps(records,indent=2),encoding='utf-8')
idc.qexit(0)
