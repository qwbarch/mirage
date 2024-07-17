@echo off

set CONDA_ENV="mirage-python-env"

rem Create the conda environment if it doesn't exist yet.
pushd ..\lib\python

conda env list | findstr /r "^%CONDA_ENV%" > nul
if %errorlevel% neq 0 (
    conda env create -f environment.yml -y
)

rem Create the python exe if it doesn't exist yet.
if not exist "dist\main\main.exe" (
    conda run -n "%CONDA_ENV%" --no-capture-output pyinstaller main.spec --noconfirm
)

popd

rem Create the rust dll if it doesn't exist yet.
pushd  ..\lib\rust

if not exist "target\debug\bertlib.dll" (
    cargo build
)

popd