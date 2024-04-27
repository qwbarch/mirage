@echo off 

rmdir /s /q "../bin"
mkdir "../bin"
call cl /Fo../bin/ /nologo /MT /LD /Fe../bin/SileroVAD.API.dll /I ../../../lib/onnxruntime/include ../lib/silero-vad.c /link /LIBPATH:../../../lib/onnxruntime/lib onnxruntime.lib
del "../bin/silero-vad.obj"