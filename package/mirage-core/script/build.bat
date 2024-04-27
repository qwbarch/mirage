@echo off

dotnet build ../src/Mirage.fsproj
rmdir /s /q "..\bin"
mkdir "..\bin"
move "..\src\bin\Debug\netstandard2.1\Mirage.Core.dll" "..\bin\Mirage.Core.dll"