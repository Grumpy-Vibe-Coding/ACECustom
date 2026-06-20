@echo off
REM Sync custom creature SQLs from Content folder to Database/Updates for build inclusion
REM Usage: Run this before building to ensure custom creatures are in the ACEBuild pipeline

setlocal enabledelayedexpansion

set SOURCE=..\Content\sql\weenies\custom\
set DEST=.\Updates\World\

echo.
echo Syncing custom creatures from Content to Database/Updates...
echo Source: %SOURCE%
echo Dest: %DEST%
echo.

robocopy "%SOURCE%" "%DEST%" *.sql /MIR /R:1 /W:1

if errorlevel 1 (
    echo.
    echo Robocopy completed with code !ERRORLEVEL!
) else (
    echo.
    echo Sync complete!
)

echo.
pause
