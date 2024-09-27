@echo off

pushd ..\..\silero-vad\script
call build-lib.bat || exit /b
call build.bat || exit /b
popd

call build.bat

rem Remove the previously packaged files.
rmdir /s /q ..\bin

rem Prepare Mirage.Core package.
mkdir ..\bin\BepInEx\core\Mirage.Core
pushd ..\bin\BepInEx\core\Mirage.Core

set src=..\..\..\..\src\bin\Debug\netstandard2.1

copy %src%\FSharp.Control.AsyncSeq.dll .
copy %src%\FSharp.Core.dll .
copy %src%\FSharpPlus.dll .
copy %src%\FSharpx.Async.dll .
rem copy %src%\FSharpx.Collections.dll .
rem copy %src%\MathNet.Numerics.dll .
rem copy %src%\MathNet.Numerics.FSharp.dll .

rem Prepare SileroVAD files.
mkdir SileroVAD
pushd SileroVAD

set src=..\..\..\..\..\silero-vad\bin

copy ..\%src%\SileroVAD.API.dll .
copy ..\%src%\SileroVAD.dll .

set src=..\..\..\..\..\..\..\lib\onnxruntime
copy %src%\lib\onnxruntime.dll .
copy %src%\lib\onnxruntime.lib .
copy %src%\lib\onnxruntime_providers_shared.dll .
copy %src%\lib\onnxruntime_providers_shared.lib .

set src=..\..\..\..\..\..\..\model\silero-vad
copy %src%\lang_dict_95.json .
copy %src%\lang_group_dict_95.json .
copy %src%\silero_vad.jit .
copy %src%\silero_vad.onnx .

popd

rem Move to the bin folder.
popd
pushd ..\bin

rem Create the Mirage.Core package.
powershell Compress-Archive^
    -Force^
    -Path "BepInEx",^
          "../manifest.json",^
          "../icon.png",^
          "../README.md"^
    -DestinationPath "../bin/mirage-core.zip"


popd