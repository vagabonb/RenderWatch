@echo off
chcp 65001 >nul
title RENDER WATCH - Р Р°Р·Р±Р»РѕРєРёСЂРѕРІРєР° Windows SmartScreen
echo ================================================================
echo   РЎРЅСЏС‚РёРµ РјРµС‚РєРё Р±Р»РѕРєРёСЂРѕРІРєРё Windows SmartScreen (Mark-of-the-Web)
echo ================================================================
echo.
echo Р Р°Р·Р±Р»РѕРєРёСЂРѕРІРєР° С„Р°Р№Р»РѕРІ РїСЂРѕРіСЂР°РјРјС‹...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -Path '%~dp0' -Recurse | Unblock-File"
echo [OK] Р¤Р°Р№Р»С‹ СѓСЃРїРµС€РЅРѕ СЂР°Р·Р±Р»РѕРєРёСЂРѕРІР°РЅС‹!
echo.
echo Р—Р°РїСѓСЃРє RENDER WATCH...
start "" "%~dp0RenderWatch.exe"