@echo off
rem Necromancer re-translation loop: update repo, then start scripts\retrans-loop.ps1 in a hidden window (no autostart).
rem ASCII only on purpose (cmd misreads UTF-8). Already running = the new one exits at once.
cd /d "%~dp0.."
git pull -q --rebase --autostash origin necromancer
del /q "C:\vh\necro-kr\loop\stop" 2>nul
start "" /min powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0retrans-loop.ps1"
echo Started. Progress: docs\retrans-status.md on GitHub. Stop: scripts\retrans-stop.bat
timeout /t 5 >nul
