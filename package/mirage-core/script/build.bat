@echo off

dotnet build ../src/Mirage.fsproj
rmdir /s /q "..\bin"
mkdir "..\bin"
move "..\src\bin\Debug\netstandard2.1\mirage-core.dll" "..\bin\mirage-core.dll"