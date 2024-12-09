@echo off

call ./build.bat
powershell Compress-Archive^
    -Force^
    -Path "../bin/*",^
          "../manifest.json",^
          "../icon.png",^
          "../../../README.md",^
          "../../../CHANGELOG.md",^
          "../../../LICENSE"^
    -DestinationPath "../bin/mirage.zip"