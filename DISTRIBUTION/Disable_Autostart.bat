@echo off
chcp 65001 >nul
title RENDER WATCH - РћС‚РєР»СЋС‡РµРЅРёРµ Р°РІС‚РѕР·Р°РїСѓСЃРєР°
echo ================================================================
echo   RENDER WATCH: РЈРґР°Р»РµРЅРёРµ РёР· Р°РІС‚РѕР·Р°РїСѓСЃРєР° Windows
echo ================================================================
echo.
echo РЈРґР°Р»РµРЅРёРµ Р·Р°РґР°С‡Рё РёР· РџР»Р°РЅРёСЂРѕРІС‰РёРєР° Windows...
schtasks /delete /tn "RenderWatch" /f >nul 2>&1
echo.
echo РЈРґР°Р»РµРЅРёРµ Р·Р°РїРёСЃРё РёР· СЂРµРµСЃС‚СЂР° Р°РІС‚РѕР·Р°РїСѓСЃРєР°...
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "RenderWatch" /f >nul 2>&1
echo.
echo [OK] RENDER WATCH СѓСЃРїРµС€РЅРѕ СѓРґР°Р»РµРЅ РёР· Р°РІС‚РѕР·Р°РїСѓСЃРєР°.
echo.
timeout /t 3 >nul