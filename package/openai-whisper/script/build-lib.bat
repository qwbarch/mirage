@echo off

cd ../lib
call conda activate whisper
pyinstaller main.spec --noconfirm
powershell -Command "conda run -n whisper COPY \"%CONDA_PREFIX%\bin\nvrtc-builtins64_121.dll\" .\dist\main\_internal\nvrtc-builtins64_121.dll"
cd ../script