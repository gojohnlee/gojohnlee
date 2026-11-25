@echo off
title zippoom collector
powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol='Tls12'; $ProgressPreference='SilentlyContinue'; $p=Join-Path $env:TEMP 'zippoom_pc.ps1'; Invoke-WebRequest 'https://raw.githubusercontent.com/gojohnlee/gojohnlee/house/zippoom_pc.ps1' -OutFile $p -UseBasicParsing; & $p"
pause
