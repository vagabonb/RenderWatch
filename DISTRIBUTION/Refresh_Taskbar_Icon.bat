@echo off
chcp 65001 >nul
title RENDER WATCH - РЎР±СЂРѕСЃ РєСЌС€Р° РёРєРѕРЅРѕРє Windows
echo ================================================================
echo   RENDER WATCH: РћС‡РёСЃС‚РєР° РєСЌС€Р° РёРєРѕРЅРѕРє РїР°РЅРµР»Рё Р·Р°РґР°С‡ Windows
echo ================================================================
echo.
echo РћР±РЅРѕРІР»РµРЅРёРµ РєСЌС€Р° Рё СѓРІРµРґРѕРјР»РµРЅРёРµ РџСЂРѕРІРѕРґРЅРёРєР°...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public class ShellNotify { [DllImport(\"\"shell32.dll\"\")] public static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2); }'; [ShellNotify]::SHChangeNotify(0x08000000, 0x0000, [IntPtr]::Zero, [IntPtr]::Zero); ie4uinit.exe -show"
echo [OK] РљСЌС€ РёРєРѕРЅРѕРє СѓСЃРїРµС€РЅРѕ РѕР±РЅРѕРІР»РµРЅ! РРєРѕРЅРєР° РЅР° РїР°РЅРµР»Рё Р·Р°РґР°С‡ РѕР±РЅРѕРІР»РµРЅР°.
timeout /t 3 >nul