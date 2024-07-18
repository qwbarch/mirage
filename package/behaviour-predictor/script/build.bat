@echo off

dotnet build ../embedding/Embedding.fsproj
dotnet build ../src/Predictor.fsproj
rmdir /s /q "..\bin"
mkdir "..\bin"
move "..\src\bin\Debug\netstandard2.1\Behaviour.Predictor.dll" "..\bin\Behaviour.Predictor.dll"
move "..\embedding\bin\Debug\netstandard2.1\Embedding.dll" "..\bin\Embedding.dll"