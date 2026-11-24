#!/usr/bin/env python3
"""Inspect PE native imports (regular+delay) and Mac load commands. No execution."""
import pathlib,subprocess,re,json,hashlib
from pe_imports import read_imports
root=pathlib.Path(__file__).resolve().parents[1]
import sys,xml.etree.ElementTree as ET
version=ET.parse(root/"Directory.Build.props").findtext(".//Version")
tag=sys.argv[1] if len(sys.argv)>1 else "alpha"+version.rsplit(".",1)[1]
win=root/('artifacts/'+tag+'-win-x64');mac=root/('artifacts/'+tag+'-osx-arm64')
known={'kernel32.dll','user32.dll','gdi32.dll','ole32.dll','oleaut32.dll','advapi32.dll','shell32.dll','shlwapi.dll','comdlg32.dll','comctl32.dll','ws2_32.dll','bcrypt.dll','crypt32.dll','ncrypt.dll','secur32.dll','ntdll.dll','version.dll','winmm.dll','imm32.dll','dwmapi.dll','uxtheme.dll','opengl32.dll','d3d11.dll','dxgi.dll','dwrite.dll','d2d1.dll','d3dcompiler_47.dll','fonts u b.dll'.replace(' ',''),'rpcrt4.dll','iphlpapi.dll','normaliz.dll','msimg32.dll','winhttp.dll','urlmon.dll','propsys.dll','dbghelp.dll','psapi.dll','mswsock.dll','wtsapi32.dll','netapi32.dll','wintrust.dll','imagehlp.dll','setupapi.dll','userenv.dll','ucrtbase.dll','dnsapi.dll','winspool.drv','powrprof.dll','cabinet.dll','msasn1.dll','wininet.dll','hid.dll','kernelbase.dll','win32u.dll','msvcrt.dll','sspicli.dll','mscoree.dll','usp10.dll','d3d12.dll','icu.dll'}
files={str(p.relative_to(win)).lower():p for p in win.rglob('*') if p.suffix.lower() in {'.exe','.dll'}}
entries=[];unresolved=set()
for name,p in files.items():
 r=subprocess.run(['x86_64-w64-mingw32-objdump','-p',str(p)],capture_output=True,text=True)
 if r.returncode:
  r=subprocess.run(['xcrun','llvm-objdump','-p',str(p)],capture_output=True,text=True)
 parsed=read_imports(p)
 imports=parsed['regular']+parsed['delay']
 secondary=re.findall(r'DLL Name:\s*(\S+)',r.stdout)
 assert r.returncode==0 and {v.lower() for v in secondary}.issubset({v.lower() for v in imports}),name
 deps=[]
 for imp in sorted(set(imports),key=str.lower):
  low=imp.lower();kind='app-local' if (low in files or str(p.relative_to(win).parent/imp).lower() in files) else 'windows-api-set' if low.startswith(('api-ms-win-','ext-ms-win-')) else 'windows-system-1903-or-newer' if low=='icu.dll' else 'windows-system' if low in known else 'UNRESOLVED'
  if kind=='UNRESOLVED':unresolved.add(imp)
  deps.append({'name':imp,'resolution':kind})
 entries.append({'name':name,'peParsed':True,'machine':parsed['machine'],'regularImports':parsed['regular'],'delayImports':parsed['delay'],'sha256':hashlib.sha256(p.read_bytes()).hexdigest(),'imports':deps,'objdumpExit':r.returncode})
macs=[]
for p in mac.iterdir():
 if p.suffix=='.dylib' or p.name=='Forterm':
  out=subprocess.check_output(['otool','-L',str(p)],text=True);macs.append({'name':p.name,'loadCommands':out})
result={'version':version,'notRuntimeVerification':True,'cleanWindowsTestPerformed':False,'regularAndDelayImportTableEvidence':entries,'unresolved':sorted(unresolved),'mac':macs,'managedPInvokeNotes':{'Windows':['advapi32.dll: CredReadW/CredWriteW/CredDeleteW/CredFree','kernel32.dll: local file handle identity'],'macOS':['Security.framework: SecItem* (symbols loaded, credential read/write/delete not tested)','CoreFoundation.framework: CF dictionary/data/string','libSystem.B.dylib: lstat (owned-file identity tested)'],'dynamicCallsNotEquivalentToCleanWindowsTest':True,'versionSensitiveImports':{'icu.dll':'Combined ICU is provided by Windows 10 1903+. Delay-imported by bundled OpenConsole; older Windows 10 must be verified, not claimed supported.'},'peFormatReference':'https://learn.microsoft.com/en-us/windows/win32/debug/pe-format'}}
(root/'evidence/native-imports.json').write_text(json.dumps(result,indent=2)+'\n')
print(json.dumps({'peFiles':len(entries),'unresolved':sorted(unresolved),'macNative':len(macs)},indent=2))
