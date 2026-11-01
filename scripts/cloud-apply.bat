@echo off
rem Put the cloud re-translation into the game (see cloud-apply.ps1).
cd /d "%~dp0.."
git pull -q --rebase --autostash origin necromancer
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0cloud-apply.ps1"
pause
