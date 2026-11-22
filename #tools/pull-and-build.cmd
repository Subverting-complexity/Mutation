@echo off
REM ===========================================================================
REM  pull-and-build.cmd
REM
REM  Pulls the latest version of the repository (with git-pull.cmd), builds
REM  the whole solution in Release, and returns the folder that holds the
REM  built Mutation.Ui.exe, so that another script can copy it.
REM
REM  Standard output carries ONE line only: the output folder path, with no
REM  trailing backslash. All progress messages go to standard error, so they
REM  still show on screen but never mix into the path. The full build log is
REM  in logs\pull-and-build.log.
REM
REM  Exit code: 0 on success, 1 when the pull or the build failed. On failure,
REM  standard output is empty.
REM
REM  Two ways to get the path from another script:
REM
REM    1. CALL it. It sets MUTATION_BUILD_OUTPUT in the caller's environment.
REM
REM         call "Z:\path\to\Mutation\#tools\pull-and-build.cmd" >nul
REM         if errorlevel 1 exit /b 1
REM         robocopy "%MUTATION_BUILD_OUTPUT%" "C:\Some\Install\Folder" /MIR
REM
REM    2. Capture standard output with FOR /F.
REM
REM         set "OUT="
REM         for /f "delims=" %%D in ('call "Z:\path\to\Mutation\#tools\pull-and-build.cmd"') do set "OUT=%%D"
REM         if not defined OUT exit /b 1
REM
REM  It pauses at the end only when double-clicked.
REM ===========================================================================

setlocal EnableDelayedExpansion

REM Work from the repository root, wherever this was started from.
cd /d "%~dp0.."

REM Explorer starts a double-clicked script as: cmd.exe /c ""<full path>" "
REM Compare only what follows /c, with spaces removed, because CMD does not
REM always put the same spacing before /c. A calling script never matches,
REM so it never hangs on a pause.
set Q=^"
set "INTERACTIVE="
set "CMD_TAIL=!CMDCMDLINE:*/c =!"
set "DOUBLE_CLICK_TAIL=!Q!!Q!%~f0!Q! !Q!"
if /i "!CMD_TAIL: =!"=="!DOUBLE_CLICK_TAIL: =!" set "INTERACTIVE=1"

set "LOG=%CD%\logs\pull-and-build.log"
if not exist "logs" mkdir "logs"

REM --- 1. Pull ---------------------------------------------------------------
REM git-pull.cmd prints progress on standard output; send it to standard error.
call "%~dp0git-pull.cmd" 1>&2
if errorlevel 1 (
	1>&2 echo.
	1>&2 echo Nothing was built, because the pull failed.
	if defined INTERACTIVE pause
	exit /b 1
)

REM --- 2. Build --------------------------------------------------------------
where dotnet >nul 2>&1
if errorlevel 1 (
	1>&2 echo The .NET SDK was not found. Install it from https://dotnet.microsoft.com/download
	if defined INTERACTIVE pause
	exit /b 1
)

1>&2 echo.
1>&2 echo Building the solution in Release. This can take a few minutes...
1>&2 echo The full build log goes to "%LOG%".

REM NoSummary stops MSBuild repeating every error in a summary at the end,
REM so the error list shown below has each error once.
dotnet build "Mutation.slnx" --configuration Release -nologo -consoleLoggerParameters:NoSummary > "%LOG%" 2>&1
if errorlevel 1 (
	1>&2 echo.
	1>&2 echo The build failed. The errors are:
	1>&2 echo.
	findstr /i /c:": error " "%LOG%" 1>&2
	1>&2 echo.
	1>&2 echo If a file is "in use", close any copy of Mutation that runs from the build folder.
	1>&2 echo The full log is in "%LOG%".
	if defined INTERACTIVE pause
	exit /b 1
)

REM --- 3. Find the output folder ---------------------------------------------
REM Ask MSBuild instead of hard-coding the path, so a change of target
REM framework or platform in the project file cannot break callers.
REM Inside FOR /F, CMD reads a bare = as a separator, so each = is escaped.
set "OUTPUT_DIR="
for /f "usebackq delims=" %%D in (`dotnet msbuild "Mutation.Ui\Mutation.Ui.csproj" -nologo -getProperty:TargetDir -p:Configuration^=Release -p:Platform^=x64`) do set "OUTPUT_DIR=%%D"

REM Drop the trailing backslash: "C:\folder\" breaks robocopy and xcopy,
REM because the backslash escapes the closing quote.
if defined OUTPUT_DIR if "!OUTPUT_DIR:~-1!"=="\" set "OUTPUT_DIR=!OUTPUT_DIR:~0,-1!"

if not exist "!OUTPUT_DIR!\Mutation.Ui.exe" (
	1>&2 echo.
	1>&2 echo The build reported success, but Mutation.Ui.exe was not found in:
	1>&2 echo "!OUTPUT_DIR!"
	if defined INTERACTIVE pause
	exit /b 1
)

1>&2 echo.
1>&2 echo The build is done. The output folder is:

REM The one line on standard output.
echo !OUTPUT_DIR!

if defined INTERACTIVE pause

REM Hand the path to a caller that used CALL. ENDLOCAL and SET run on one
REM line, so the value is expanded before the local environment is dropped.
endlocal & set "MUTATION_BUILD_OUTPUT=%OUTPUT_DIR%"
exit /b 0
