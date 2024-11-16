@echo off

call ./build.bat
mkdir "../bin/BepInEx/plugins"
powershell Move-Item -Path "../bin/MirageRevive.dll" -Destination "../bin/BepInEx/plugins/MirageRevive.dll"
powershell Compress-Archive^
    -Force^
    -Path "../bin/BepInEx",^
          "../manifest.json",^
          "../icon.png",^
          "../README.md",^
          "../CHANGELOG.md",^
          "../../../LICENSE"^
    -DestinationPath "../bin/mirage-revive.zip"