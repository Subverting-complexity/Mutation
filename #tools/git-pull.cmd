@echo off
REM ===========================================================================
REM  git-pull.cmd
REM
REM  Pulls the latest version of the whole Mutation repository from GitHub
REM  into this checkout, on whatever branch is checked out.
REM
REM  It only fast-forwards. If you have local commits that are not on the
REM  server, or local changes that would be overwritten, it stops and tells
REM  you instead of creating a merge.
REM
REM  Double-click it, or run it from a command prompt or another script.
REM  It pauses at the end only when double-clicked.
REM
REM  Exit code: 0 when the pull worked, 1 when it did not.
REM ===========================================================================

setlocal EnableDelayedExpansion

REM Work from the repository root, wherever this was started from.
cd /d "%~dp0.."

REM Explorer starts a double-clicked script as: cmd.exe /c ""<full path>" "
REM Compare only what follows /c, with spaces removed, because CMD does not
REM always put the same spacing before /c. A script that calls this one
REM (directly, with CALL, or inside FOR /F) never matches, so never hangs.
set Q=^"
set "INTERACTIVE="
set "CMD_TAIL=!CMDCMDLINE:*/c =!"
set "DOUBLE_CLICK_TAIL=!Q!!Q!%~f0!Q! !Q!"
if /i "!CMD_TAIL: =!"=="!DOUBLE_CLICK_TAIL: =!" set "INTERACTIVE=1"

where git >nul 2>&1
if errorlevel 1 (
	echo Git was not found. Install it from https://git-scm.com and run this again.
	if defined INTERACTIVE pause
	exit /b 1
)

for /f "delims=" %%B in ('git branch --show-current') do set "BRANCH=%%B"
echo Pulling the latest version of branch "!BRANCH!" into %CD% ...
echo.

git pull --ff-only
if errorlevel 1 (
	echo.
	echo The pull failed. Read the Git message above.
	echo Local changes or local commits that are not on the server are the usual cause.
	if defined INTERACTIVE pause
	exit /b 1
)

echo.
echo The pull is done.
if defined INTERACTIVE pause
exit /b 0
