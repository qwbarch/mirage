@echo off

set ENV_NAME="mirage-python-env"

pushd ..\lib\python

rem Create the conda environment if it doesn't exist yet.
conda env list | findstr /r "^%ENV_NAME%" > nul
if %errorlevel% neq 0 (
    conda env create -f environment.yml -y
)

rem Create the python exe if it doesn't exist yet.
if not exist "dist\main\main.exe" (
    conda run -n "%ENV_NAME%" --no-capture-output pyinstaller main.spec --noconfirm
)

popd