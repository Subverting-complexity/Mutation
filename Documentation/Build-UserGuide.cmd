@echo off
REM ===========================================================================
REM  Build-UserGuide.cmd
REM
REM  Rebuilds the HTML version of the Mutation user guide from the Markdown
REM  files in .\UserGuide\markdown, writing the result into .\UserGuide\html.
REM
REM  The Markdown is always the source of truth. Edit the .md files, run this,
REM  and never edit the generated .html by hand.
REM
REM  Conversion is done by pandoc.exe, which is looked for in this folder first
REM  and then on PATH.
REM
REM  Just double-click this file, or run it from a command prompt.
REM ===========================================================================

setlocal
cd /d "%~dp0"

REM Pause at the end only when double-clicked, so the window stays open long
REM enough to read. When run from a prompt or a script, finish and return.
REM CMDCMDLINE is quoted before echoing: an unquoted path containing & or |
REM would otherwise be parsed as a command separator.
set "INTERACTIVE="
echo "%CMDCMDLINE%" | find /i "%~nx0" >nul 2>&1 && set "INTERACTIVE=1"

echo.
echo Building the Mutation user guide...
echo.

set "PS_EXE=powershell.exe"
where pwsh.exe >nul 2>&1 && set "PS_EXE=pwsh.exe"

"%PS_EXE%" -NoProfile -NoLogo -ExecutionPolicy Bypass -File "%~dp0Convert-UserGuide.ps1"
set "RESULT=%ERRORLEVEL%"

echo.
if not "%RESULT%"=="0" (
	echo Build FAILED. See the messages above.
	echo.
	if defined INTERACTIVE pause
	endlocal
	exit /b %RESULT%
)

echo Build finished successfully.
echo.
if defined INTERACTIVE pause
endlocal
exit /b 0
