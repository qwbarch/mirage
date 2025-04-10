@echo off

dotnet build ../src/Mirage.fsproj
dotnet build ../plugin/Plugin.csproj
rmdir /s /q "..\bin"
mkdir "..\bin"
copy "..\plugin\bin\x64\Debug\netstandard2.1\Mirage.Plugin.dll" "..\bin\Mirage.Plugin.dll"
copy "..\src\bin\x64\Debug\netstandard2.1\Mirage.dll" "..\bin\Mirage.dll"
copy "..\src\bin\x64\Debug\netstandard2.1\Mirage.Core.dll" "..\bin\Mirage.Core.dll"
copy "..\src\bin\x64\Debug\netstandard2.1\Mirage.Compatibility.dll" "..\bin\Mirage.Compatibility.dll"
copy "..\..\..\lib\Mirage.unity3d" "..\bin\Mirage.unity3d"