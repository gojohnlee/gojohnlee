@echo off
rem 이 파일은 CP949로 저장한다(cmd가 UTF-8 한글을 깨뜨림). 고칠 땐 UTF-8로 쓰고 iconv -t cp949로 바꿀 것.
setlocal EnableDelayedExpansion
title 네크로맨서 테일 한글 폰트 설치
set "HERE=%~dp0"
set "GAME="
if exist "%HERE%gamepath.txt" for /f "usebackq delims=" %%P in ("%HERE%gamepath.txt") do if not defined GAME if exist "%%P\TheNecromancer.exe" set "GAME=%%P"
if not defined GAME for /f "tokens=2,*" %%A in ('reg query "HKCU\Software\Valve\Steam" /v SteamPath 2^>nul ^| findstr /i SteamPath') do set "STEAM=%%B"
if not defined GAME if defined STEAM (
  set "STEAM=!STEAM:/=\!"
  if exist "!STEAM!\steamapps\common\The Necromancer\TheNecromancer.exe" set "GAME=!STEAM!\steamapps\common\The Necromancer"
  if not defined GAME for /f usebackq^ tokens^=4^ delims^=^" %%L in (`findstr /i /c:\"path\" "!STEAM!\steamapps\libraryfolders.vdf"`) do (
    set "LIB=%%L" & set "LIB=!LIB:\=\!"
    if not defined GAME if exist "!LIB!\steamapps\common\The Necromancer\TheNecromancer.exe" set "GAME=!LIB!\steamapps\common\The Necromancer"
  )
)
if not defined GAME (
  echo 게임 폴더를 못 찾았습니다. 이 폴더에 gamepath.txt를 만들고 게임 폴더 경로를 한 줄로 적은 뒤 다시 실행하세요.
  goto :end
)
echo 게임 폴더: %GAME%
copy /y "%HERE%dist\winhttp.dll" "%GAME%\" >nul || goto :fail
copy /y "%HERE%dist\doorstop_config.ini" "%GAME%\" >nul || goto :fail
if not exist "%GAME%\KoreanFont" mkdir "%GAME%\KoreanFont"
copy /y "%HERE%dist\KoreanFont\NecroKoreanFont.dll" "%GAME%\KoreanFont\" >nul || goto :fail
copy /y "%HERE%dist\KoreanFont\ui-ko.tsv" "%GAME%\KoreanFont\" >nul || goto :fail
echo 설치 끝. 게임을 켜고 "Localization file found ... Korean.csv" 창에서 Yes를 누르세요.
goto :end
:fail
echo [실패] 파일을 복사하지 못했습니다. 게임을 끄고 다시 실행하세요.
:end
echo.
pause
