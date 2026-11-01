@echo off
rem Stops the re-translation loop after the current chunk (creates C:\vh\necro-kr\loop\stop).
echo stop> "C:\vh\necro-kr\loop\stop"
echo Will stop after the current chunk (a few minutes).
timeout /t 5 >nul
