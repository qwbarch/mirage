@echo off

call ./build.bat
mkdir "../bin/BepInEx/plugins"
powershell Move-Item -Path "../bin/Mirage.dll" -Destination "../bin/BepInEx/plugins/Mirage.dll"
powershell Move-Item -Path "../bin/Mirage.Core.dll" -Destination "../bin/BepInEx/plugins/Mirage.Core.dll"
powershell Move-Item -Path "../bin/Mirage.Compatibility.dll" -Destination "../bin/BepInEx/plugins/Mirage.Compatibility.dll"
powershell Compress-Archive^
    -Force^
    -Path "../bin/BepInEx",^
          "../manifest.json",^
          "../icon.png",^
          "../../../README.md",^
          "../../../CHANGELOG.md",^
          "../../../LICENSE"^
    -DestinationPath "../bin/mirage.zip"
