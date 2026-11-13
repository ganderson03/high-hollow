@echo off
rem Double-click to play: the server plus two game windows. Options pass through, e.g.
rem   play -Players 1      play -Level hollow      play -Build
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0play.ps1" %*
if errorlevel 1 pause
