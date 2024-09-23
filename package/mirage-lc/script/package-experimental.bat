@echo off

call ./build.bat
mkdir "../bin/BepInEx/plugins"
powershell Move-Item -Path "../bin/Mirage.dll" -Destination "../bin/BepInEx/plugins/Mirage.dll"
powershell Move-Item -Path "../bin/Mirage.Core.dll" -Destination "../bin/BepInEx/plugins/Mirage.Core.dll"
powershell Copy-Item -Path "../manifest-experimental.json" -Destination "../bin/manifest.json"
powershell Compress-Archive^
    -Force^
    -Path "../bin/BepInEx",^
          "../bin/manifest.json",^
          "../icon.png",^
          "../../../README.md",^
          "../../../CHANGELOG.md",^
          "../../../LICENSE"^
    -DestinationPath "../bin/mirage.zip"
