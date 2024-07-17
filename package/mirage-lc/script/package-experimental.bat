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

pushd ..\src
dotnet build
popd

rem Remove the previously packaged files.
rmdir /s /q ..\bin

rem Prepare the Mirage.Core package.
mkdir ..\bin\Mirage.Core\BepInEx\core\Mirage.Core
pushd ..\bin\Mirage.Core\BepInEx\core\Mirage.Core

set src=..\..\..\..\..\src\bin\Debug\netstandard2.1

copy %src%\FSharp.Control.AsyncSeq.dll .
copy %src%\FSharp.Core.dll .
copy %src%\FSharpPlus.dll .
copy %src%\FSharpx.Async.dll .
copy %src%\FSharpx.Collections.dll .
copy %src%\MathNet.Numerics.dll .
copy %src%\MathNet.Numerics.FSharp.dll .

rem Prepare SileroVAD files.
mkdir SileroVAD
pushd SileroVAD

copy ..\%src%\SileroVAD.API.dll .
copy ..\%src%\SileroVAD.dll .

set src=..\..\..\..\..\..\..\..\lib\onnxruntime
copy %src%\lib\onnxruntime.dll .
copy %src%\lib\onnxruntime.lib .
copy %src%\lib\onnxruntime_providers_shared.dll .
copy %src%\lib\onnxruntime_providers_shared.lib .

set src=..\..\..\..\..\..\..\..\model\silero-vad
copy %src%\lang_dict_95.json .
copy %src%\lang_group_dict_95.json .
copy %src%\silero_vad.jit .
copy %src%\silero_vad.onnx .

popd

rem Create the Mirage.Core package.
popd
powershell Compress-Archive^
    -Force^
    -Path "..\bin\Mirage.Core",^
          "..\..\mirage-core\manifest.json",^
          "..\..\mirage-core\icon.png",^
          "..\..\..\README.md",^
          "..\..\..\LICENSE"^
    -DestinationPath "..\bin\Mirage.Core.zip"

rem Prepare the Mirage package.
mkdir ..\bin\Mirage\BepInEx\plugins\Mirage
pushd ..\bin\Mirage\BepInEx\plugins\Mirage

set src=..\..\..\..\..\src\bin\Debug\netstandard2.1
copy %src%\Mirage.Core.dll .
copy %src%\Mirage.dll .

popd

rem Create the Mirage package.
powershell Compress-Archive^
    -Force^
    -Path "..\bin\Mirage",^
          "..\manifest.json",^
          "..\icon.png",^
          "..\..\..\README.md",^
          "..\..\..\LICENSE"^
    -DestinationPath "..\bin\Mirage.zip"