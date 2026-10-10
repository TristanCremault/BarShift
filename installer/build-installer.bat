@echo off
rem ============================================================
rem  Compile BarShift puis fabrique l'installeur.
rem  Double-clique sur ce fichier (il est dans le dossier "installer").
rem ============================================================
cd /d "%~dp0.."

echo.
echo [1/2] Compilation de BarShift...

set "DOTNET=dotnet"
dotnet --version >nul 2>&1
if not errorlevel 1 goto build

set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"
"%DOTNET%" --version >nul 2>&1
if not errorlevel 1 goto build

if exist "publish\BarShift.exe" goto skipbuild

echo.
echo Le SDK .NET est introuvable et il n'y a pas de publish\BarShift.exe.
echo Ouvre un terminal qui marche (celui d'avant), place-toi dans le dossier du projet et lance :
echo   dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
echo Puis relance ce fichier.
pause
exit /b 1

:skipbuild
echo.
echo ATTENTION : le SDK .NET est introuvable depuis ce fichier.
echo On utilise le publish\BarShift.exe deja compile (date : voir le dossier publish).
goto inno

:build
"%DOTNET%" publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
if errorlevel 1 goto builderror
goto inno

:builderror
echo.
echo ERREUR pendant la compilation. Copie le message ci-dessus pour obtenir de l'aide.
pause
exit /b 1

:inno
echo.
echo [2/2] Creation de l'installeur...

set "ISCC="
for %%D in ("%ProgramFiles(x86)%" "%ProgramFiles%" "%LocalAppData%\Programs") do (
    for /d %%I in ("%%~D\Inno Setup*") do (
        if exist "%%~I\ISCC.exe" set "ISCC=%%~I\ISCC.exe"
    )
)
if defined ISCC goto runiscc

echo.
echo Inno Setup est introuvable dans les dossiers habituels.
echo Dans l'Explorateur, cherche le fichier ISCC.exe puis glisse-le sur ce fichier .bat.
if "%~1"=="" goto noiscc
set "ISCC=%~1"
goto runiscc

:noiscc
pause
exit /b 1

:runiscc
"%ISCC%" "installer\BarShift.iss"
if errorlevel 1 goto isccerror

echo.
echo Termine ! Ton installeur est ici : installer\output\BarShift-Setup.exe
start "" "installer\output"
pause
exit /b 0

:isccerror
echo.
echo ERREUR pendant la creation de l'installeur.
pause
exit /b 1
