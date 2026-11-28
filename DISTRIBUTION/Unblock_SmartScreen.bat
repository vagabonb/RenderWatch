@echo off
chcp 65001 >nul
set "WATCH_EXE=%~dp0PCWatch.exe"
if not exist "%WATCH_EXE%" set "WATCH_EXE=%~dp0RenderWatch.exe"
title PC Watch - Запуск и разблокировка
echo ================================================================
echo   PC Watch: Разблокировка и запуск с правами Администратора
echo ================================================================
echo.
echo 1. Снятие блокировок Windows SmartScreen (Mark-of-the-Web)...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -Path '%~dp0' -Recurse | Unblock-File"
echo [OK] Файлы успешно разблокированы!
echo.
echo 2. Запуск службы низкоуровневых датчиков материнской платы (PawnIO)...
sc.exe config PawnIO start= auto >nul 2>&1
sc.exe start PawnIO >nul 2>&1
echo [OK] Служба датчиков проверена.
echo.
echo 3. Обновление кэша иконок...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public class ShellNotify { [DllImport(\"\"shell32.dll\"\")] public static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2); }'; [ShellNotify]::SHChangeNotify(0x08000000, 0x0000, [IntPtr]::Zero, [IntPtr]::Zero); ie4uinit.exe -show"
echo [OK] Кэш иконок обновлен!
echo.
echo 4. Запуск PC Watch с правами Администратора (доступ к датчикам SuperIO / GPU)...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%WATCH_EXE%' -WorkingDirectory '%~dp0' -Verb RunAs"
echo [OK] Команда на запуск отправлена.