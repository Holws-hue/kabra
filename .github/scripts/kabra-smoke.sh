#!/usr/bin/env bash
set -uo pipefail
mkdir -p smoke-results
echo "ANDROID_EMULATOR_ABIS" | tee smoke-results/report.txt
adb shell getprop ro.product.cpu.abilist | tee -a smoke-results/report.txt
echo "APK_INSTALL" | tee -a smoke-results/report.txt
if ! adb install -r apk/Kabra_Online_2D_V48_LAN_ARM64_TEST.apk 2>&1 | tee -a smoke-results/report.txt; then
  echo "APK_INSTALL_FAILED (may be missing ARM64 native translation)" | tee -a smoke-results/report.txt
  exit 1
fi
adb logcat -c || true
echo "APP_START" | tee -a smoke-results/report.txt
adb shell monkey -p com.kabra.online2d.lanfix3 -c android.intent.category.LAUNCHER 1 2>&1 | tee -a smoke-results/report.txt
sleep 12
adb exec-out screencap -p > smoke-results/01-start.png || true
adb shell uiautomator dump /sdcard/kabra_ui.xml || true
adb pull /sdcard/kabra_ui.xml smoke-results/01-ui.xml || true
adb shell dumpsys meminfo com.kabra.online2d.lanfix3 > smoke-results/01-meminfo.txt || true
echo "ATTEMPT_LAN_START_ON_EMULATOR_LOOPBACK_BRIDGE_10.0.2.2" | tee -a smoke-results/report.txt
python3 - <<'PY'
import subprocess,xml.etree.ElementTree as ET,re
def adb(*args): return subprocess.run(['adb','shell',*args],text=True,capture_output=True)
try:
    root=ET.parse('smoke-results/01-ui.xml').getroot()
    edit=[n for n in root.iter('node') if n.attrib.get('class') == 'android.widget.EditText']
    buttons=[n for n in root.iter('node') if 'INICIAR KABRA EN LAN' in n.attrib.get('text','')]
    def tap(node):
        m=re.findall(r'\[(\d+),(\d+)\]',node.attrib['bounds'])
        x=(int(m[0][0])+int(m[1][0]))//2
        y=(int(m[0][1])+int(m[1][1]))//2
        adb('input','tap',str(x),str(y))
    print('edit fields',len(edit),'start buttons',len(buttons))
    if len(edit)>=2 and buttons:
        tap(edit[0]);adb('input','text','10.0.2.2')
        tap(edit[1]);adb('input','text','5555')
        adb('input','keyevent','4')
        tap(buttons[0])
        print('clicked LAN_START')
    else: print('NO_EDIT_FIELDS_OR_START_BUTTON')
except Exception as ex: print('UI_AUTOMATION_ERROR',repr(ex))
PY
sleep 45
adb exec-out screencap -p > smoke-results/02-after-launch.png || true
adb shell uiautomator dump /sdcard/kabra_ui.xml || true
adb pull /sdcard/kabra_ui.xml smoke-results/02-ui.xml || true
adb shell dumpsys meminfo com.kabra.online2d.lanfix3 > smoke-results/02-meminfo.txt || true
adb logcat -d -v time > smoke-results/02-all-logcat.txt || true
grep -E 'KabraBootstrap|KabraLAN|AndroidRuntime|MonoGame|FATAL_MANAGED_EXCEPTION|GAME_FIRST_RENDER_SUCCESS|SHADER' smoke-results/02-all-logcat.txt | tail -n 180 > smoke-results/02-kabra-logcat.txt || true
echo "FINAL_RESULT" | tee -a smoke-results/report.txt
if grep -q 'GAME_FIRST_RENDER_SUCCESS' smoke-results/02-kabra-logcat.txt; then
  echo 'FIRST_RENDER_SUCCESS_OBSERVED' | tee -a smoke-results/report.txt
elif grep -q 'KABRA_ANDROID_V48_FIX3_SHADER_GUARD_ACTIVE' smoke-results/02-kabra-logcat.txt; then
  echo 'FIX3_BOOT_OBSERVED_BUT_NO_FIRST_RENDER' | tee -a smoke-results/report.txt
else
  echo 'NO_FIX3_BOOT_MARKER_OR_RENDER' | tee -a smoke-results/report.txt
fi
grep -E 'SHADER|EXCEPTION|GAME_FIRST_RENDER_SUCCESS|FIRST_DRAW|INTERSECT_INITIALIZED|ANDROID_DESKTOP_SHADER_SKIPPED' smoke-results/02-kabra-logcat.txt | tail -n 40 | tee -a smoke-results/report.txt || true
