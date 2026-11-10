@echo off
rem Builds gzjam_dsp.dll (Windows x64) into lib\bass\win-x64. It uses no C runtime and no
rem Windows libraries, so it needs nothing installed to run, and only the compiler itself
rem (Visual Studio's C++ tools, cl.exe) to build: run from anywhere.
setlocal
set HERE=%~dp0
for /f "usebackq delims=" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -property installationPath`) do set VS=%%i
for /d %%v in ("%VS%\VC\Tools\MSVC\*") do set BIN=%%v\bin\Hostx64\x64
if not exist "%BIN%\cl.exe" (
  echo Visual Studio's C++ tools aren't installed.
  exit /b 1
)
set OUT=%HERE%..\lib\bass\win-x64
set OBJ=%TEMP%\gzjam_dsp
if not exist "%OBJ%" mkdir "%OBJ%"
"%BIN%\cl.exe" /nologo /O2 /GS- /W4 /WX /LD /Zl "%HERE%gzjam_dsp.c" /Fo"%OBJ%\\" /Fe"%OUT%\gzjam_dsp.dll" ^
  /link /NOLOGO /NOENTRY /NODEFAULTLIB /NOIMPLIB /NOEXP || exit /b 1
del "%OUT%\gzjam_dsp.lib" "%OUT%\gzjam_dsp.exp" 2>nul
echo Built %OUT%\gzjam_dsp.dll
