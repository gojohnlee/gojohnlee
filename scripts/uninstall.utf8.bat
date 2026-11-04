@echo off
rem 이 파일은 CP949로 저장한다. 한글 폰트 모드만 지운다(한글 번역 파일·게임 파일은 그대로).
setlocal EnableDelayedExpansion
set "HERE=%~dp0"
set "GAME="
if exist "%HERE%gamepath.txt" for /f "usebackq delims=" %%P in ("%HERE%gamepath.txt") do if not defined GAME set "GAME=%%P"
if not defined GAME for /f "tokens=2,*" %%A in ('reg query "HKCU\Software\Valve\Steam" /v SteamPath 2^>nul ^| findstr /i SteamPath') do set "GAME=%%B\steamapps\common\The Necromancer"
set "GAME=!GAME:/=\!"
del /f /q "%GAME%\winhttp.dll" "%GAME%\doorstop_config.ini" >nul 2>&1
rmdir /s /q "%GAME%\KoreanFont" >nul 2>&1
rmdir /s /q "%GAME%\NecroHelper" >nul 2>&1
echo 지웠습니다: %GAME%
pause
