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
REM  Everything needed is in this repository: the converter is the small .NET
REM  project in .\UserGuideBuilder. All you need installed is the .NET SDK,
REM  which you already have if you can build Mutation itself.
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

where dotnet >nul 2>&1
if errorlevel 1 (
	echo The .NET SDK was not found.
	echo.
	echo Install it from https://dotnet.microsoft.com/download and run this again.
	echo.
	if defined INTERACTIVE pause
	endlocal
	exit /b 1
)

REM Anything after the project path that dotnet does not recognise is forwarded
REM to the tool itself, so keep this to genuine 'dotnet run' options.
dotnet run --project "%~dp0UserGuideBuilder\UserGuideBuilder.csproj" --configuration Release --verbosity quiet
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
