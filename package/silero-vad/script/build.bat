@echo off

dotnet build ../src/Silero.fsproj
rmdir /s /q "..\bin"
mkdir "..\bin"
move "..\src\bin\Debug\netstandard2.1\SileroVAD.API.dll" "..\bin\SileroVAD.API.dll"
move "..\src\bin\Debug\netstandard2.1\SileroVAD.dll" "..\bin\SileroVAD.dll"