@echo off 

where cl >nul 2>&1
if %errorlevel% NEQ 0 (
    echo Failed to build SileroVAD's C lib. Please run this script again via the "Developer Powershell".
    exit /b
)

rmdir /s /q "../bin"
mkdir "../bin"
call cl /Fo../bin/ /nologo /MT /LD /Fe../bin/SileroVAD.API.dll /I ../../../lib/onnxruntime/include ../lib/silero-vad.c /link /LIBPATH:../../../lib/onnxruntime/lib onnxruntime.lib
del "../bin/silero-vad.obj"