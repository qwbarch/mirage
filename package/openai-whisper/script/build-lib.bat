@echo off

set CONDA_ENV="mirage-whisper"

rem Create the conda environment if it doesn't exist yet.
pushd ..\lib

conda env list | findstr /r "^%CONDA_ENV%" > nul
if %errorlevel% neq 0 (
    conda env create -f environment.yml -y
)

rem Create the python exe if it doesn't exist yet.
if not exist "dist\main\main.exe" (
    conda run -n "%CONDA_ENV%" --no-capture-output pyinstaller main.spec --noconfirm
    conda run -n "%CONDA_ENV%" copy %CONDA_PREFIX%\bin\nvrtc-builtins64_118.dll dist\main\_internal\nvrtc-builtins64_118.dll"
)

popd