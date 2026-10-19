@echo off
chcp 65001 >nul
title RENDER WATCH - Р—Р°РїСѓСЃРє Рё СЂР°Р·Р±Р»РѕРєРёСЂРѕРІРєР°
echo ================================================================
echo   RENDER WATCH: Р Р°Р·Р±Р»РѕРєРёСЂРѕРІРєР° Рё Р·Р°РїСѓСЃРє СЃ РїСЂР°РІР°РјРё РђРґРјРёРЅРёСЃС‚СЂР°С‚РѕСЂР°
echo ================================================================
echo.
echo 1. РЎРЅСЏС‚РёРµ Р±Р»РѕРєРёСЂРѕРІРѕРє Windows SmartScreen (Mark-of-the-Web)...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -Path '%~dp0' -Recurse | Unblock-File"
echo [OK] Р¤Р°Р№Р»С‹ СѓСЃРїРµС€РЅРѕ СЂР°Р·Р±Р»РѕРєРёСЂРѕРІР°РЅС‹!
echo.
echo 2. РћР±РЅРѕРІР»РµРЅРёРµ РєСЌС€Р° РёРєРѕРЅРѕРє...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public class ShellNotify { [DllImport(\"\"shell32.dll\"\")] public static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2); }'; [ShellNotify]::SHChangeNotify(0x08000000, 0x0000, [IntPtr]::Zero, [IntPtr]::Zero); ie4uinit.exe -show"
echo [OK] РљСЌС€ РёРєРѕРЅРѕРє РѕР±РЅРѕРІР»РµРЅ!
echo.
echo 3. Р—Р°РїСѓСЃРє RENDER WATCH СЃ РїСЂР°РІР°РјРё РђРґРјРёРЅРёСЃС‚СЂР°С‚РѕСЂР° (РґРѕСЃС‚СѓРї Рє РґР°С‚С‡РёРєР°Рј СЏРґСЂР° MSR)...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~dp0RenderWatch.exe' -WorkingDirectory '%~dp0' -Verb RunAs"
echo [OK] РљРѕРјР°РЅРґР° РЅР° Р·Р°РїСѓСЃРє РѕС‚РїСЂР°РІР»РµРЅР°.