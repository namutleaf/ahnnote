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

set OPTS=/nologo /target:winexe /codepage:65001 /optimize+ /out:AHNNOTE.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll

echo [1/3] Building AHNNOTE.exe ...
"%CSC%" %OPTS% AhnNote.cs
if errorlevel 1 goto fail

echo [2/3] Making icon ...
if exist AHNNOTE.ico del AHNNOTE.ico
start "" /wait AHNNOTE.exe /makeicon
if not exist AHNNOTE.ico (
    echo Icon was not created. AHNNOTE.exe is built without icon.
    goto ok
)

echo [3/3] Adding icon ...
"%CSC%" %OPTS% /win32icon:AHNNOTE.ico AhnNote.cs
if errorlevel 1 (
    echo Icon step failed. Building without icon.
    "%CSC%" %OPTS% AhnNote.cs
    if errorlevel 1 goto fail
)

:ok
echo.
echo [OK] AHNNOTE.exe created.
echo Notes are saved in the AhnNoteData folder next to AHNNOTE.exe
pause
exit /b 0

:fail
echo.
echo [FAIL] Build failed. Check the messages above.
echo If AHNNOTE is running, close it and try again.
pause
exit /b 1
