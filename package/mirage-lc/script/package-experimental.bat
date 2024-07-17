@echo off

pushd ..\..\silero-vad\script
call build-lib.bat || exit /b
popd

call ./build.bat