@echo off

pushd ..\..\silero-vad\script
call build-lib.bat || exit /b
popd

pushd ..\..\openai-whisper\script
call build-lib.bat || exit /b
popd

pushd ..\..\behaviour-predictor\script
call build-lib.bat || exit /b
popd

call build.bat