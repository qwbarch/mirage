@echo off

dotnet build ../src/MirageRevive.fsproj
rmdir /s /q "..\bin"
mkdir "..\bin"
move "..\src\bin\Debug\netstandard2.1\MirageRevive.dll" "..\bin\MirageRevive.dll"