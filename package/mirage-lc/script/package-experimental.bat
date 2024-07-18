@echo off

set originalDirectory=%cd%

rem Download faster-whisper-large-v2 if it doesn't exist locally.
pushd ..\..\..\model
if not exist "whisper\model.bin" (
    echo Downloading faster-whisper-large-v2
    set ProgressPreference=SilentlyContinue
    powershell -Command "$ProgressPreference = 'SilentlyContinue'; Invoke-WebRequest -Uri 'https://huggingface.co/guillaumekln/faster-whisper-large-v2/resolve/main/model.bin' -OutFile 'whisper\model.bin'"
)
popd

pushd ..\..\silero-vad\script
call build-lib.bat || exit /b
popd

pushd ..\..\openai-whisper\script
call build-lib.bat || exit /b
popd

pushd ..\..\behaviour-predictor\script
call build-lib.bat || exit /b
popd

rem Working directory is not always guaranteed to be in the original directory. This forces it.
cd %originalDirectory%

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
pushd ..\bin\Mirage.Core
7z a ..\Mirage.Core.zip "BepInEx" "..\..\..\mirage-core\manifest.json" "..\..\..\mirage-core\icon.png" "..\..\..\..\README.md" "..\..\..\..\LICENSE"
popd
rmdir /s /q ..\bin\Mirage.Core

rem Prepare the Mirage package.
mkdir ..\bin\Mirage\BepInEx\plugins\Mirage
pushd ..\bin\Mirage\BepInEx\plugins\Mirage

set src=..\..\..\..\..\src\bin\Debug\netstandard2.1
copy %src%\Mirage.Core.dll .
copy %src%\Mirage.dll .

popd

rem Create the Mirage package.
pushd ..\bin\Mirage
7z a ..\Mirage.zip "BepInEx" "..\..\manifest.json" "..\..\icon.png" "..\..\..\..\README.md" "..\..\..\..\LICENSE"
popd
rmdir /s /q ..\bin\Mirage

rem Prepare the Mirage.AI package.
mkdir ..\bin\Mirage.AI\BepInEx\core\Mirage.AI\OpenAI.Whisper
pushd ..\bin\Mirage.AI\BepInEx\core\Mirage.AI\OpenAI.Whisper

set src=..\..\..\..\..\..\..\openai-whisper
copy %src%\bin\OpenAI.Whisper.dll .
robocopy %src%\lib\dist\main . /e /copy:DAT /xf /xd
robocopy %src%\..\..\model\whisper model /e /copy:DAT /xf /xd

popd

mkdir ..\bin\Mirage.AI\BepInEx\core\Mirage.AI\Behaviour.Predictor
pushd ..\bin\Mirage.AI\BepInEx\core\Mirage.AI\Behaviour.Predictor

set src=..\..\..\..\..\..\src\bin\Debug\netstandard2.1
copy %src%\Behaviour.Predictor.dll .
copy %src%\Embedding.dll .

set src=..\..\..\..\..\..\..\behaviour-predictor
robocopy %src%\lib\python\dist\main . /e /copy:DAT /xf /xd
copy %src%\lib\rust\target\debug\bertlib.dll .
copy %src%\lib\rust\target\debug\bertlib.pdb .

popd

rem Create the Mirage.AI package.
pushd ..\bin\Mirage.AI
7z a ..\Mirage.AI.zip "BepInEx" "..\..\..\mirage-ai\manifest.json" "..\..\..\mirage-ai\icon.png" "..\..\..\..\README.md" "..\..\..\..\LICENSE"
popd
rmdir /s /q ..\bin\Mirage.AI