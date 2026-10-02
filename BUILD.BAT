@echo off
chcp 65001 > nul
cd /d "%~dp0"

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo [ERROR] csc.exe not found. .NET Framework 4.x is required.
    pause
    exit /b 1
)

if not exist "AhnNote.cs" (
    echo [ERROR] AhnNote.cs not found in this folder.
    pause
    exit /b 1
)

echo Building AHNNOTE.exe ...
"%CSC%" /nologo /target:winexe /codepage:65001 /optimize+ /out:AHNNOTE.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll AhnNote.cs

if errorlevel 1 (
    echo.
    echo [FAIL] Build failed. Check the messages above.
    pause
    exit /b 1
)

echo.
echo [OK] AHNNOTE.exe created.
echo Notes are saved in the AhnNoteData folder next to AHNNOTE.exe
pause
