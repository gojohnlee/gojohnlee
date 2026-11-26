@echo off
chcp 65001 >nul
rem 원룸 폴더의 Gemini 결과(*.txt)를 GitHub house 브랜치 inbox/로 올린다(사용자 11-26). 매일 아침 클라우드가 합친다.
where git >nul 2>nul || (echo git이 없습니다. https://git-scm.com 에서 설치 후 다시 실행하세요. & pause & exit /b 1)
set "SRC=%~dp0"
set "REPO=%USERPROFILE%\house-sync"
if not exist "%REPO%\.git" git clone -q -b house https://github.com/gojohnlee/gojohnlee.git "%REPO%" || (echo 저장소 받기 실패 & pause & exit /b 1)
cd /d "%REPO%"
git pull -q --rebase origin house
set N=0
for %%F in ("%SRC%*.txt") do (copy /y "%%F" "inbox\" >nul & set /a N+=1 & (if not exist "%SRC%올림" mkdir "%SRC%올림") & move /y "%%F" "%SRC%올림\" >nul)
if %N%==0 (echo 올릴 txt가 없습니다. & timeout /t 5 >nul & exit /b 0)
git add inbox
git commit -q -m "원룸 폴더 Gemini 결과 올림"
git push -q origin HEAD:house || (git pull -q --rebase origin house & git push -q origin HEAD:house)
echo %N%개 올렸습니다. 내일 아침 보고에 반영됩니다.
timeout /t 5 >nul
