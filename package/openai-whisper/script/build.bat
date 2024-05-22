@echo off

dotnet build ../src/Whisper.fsproj
rmdir /s /q "..\bin"
mkdir "..\bin"
move "..\src\bin\Debug\netstandard2.1\OpenAI.Whisper.dll" "..\bin\OpenAI.Whisper.dll"