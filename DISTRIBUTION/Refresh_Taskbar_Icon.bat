@echo off
chcp 65001 >nul
title PC Watch - Сброс кэша иконок Windows
echo ================================================================
echo   PC Watch: Очистка кэша иконок панели задач Windows
echo ================================================================
echo.
echo Обновление кэша и уведомление Проводника...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public class ShellNotify { [DllImport(\"\"shell32.dll\"\")] public static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2); }'; [ShellNotify]::SHChangeNotify(0x08000000, 0x0000, [IntPtr]::Zero, [IntPtr]::Zero); ie4uinit.exe -show"
echo [OK] Кэш иконок успешно обновлен! Иконка на панели задач обновлена.
timeout /t 3 >nul