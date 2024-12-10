@echo off

call ./build.bat
powershell Copy-Item -Path "../manifest-experimental.json" -Destination "../bin/manifest.json"
powershell Compress-Archive^
    -Force^
    -Path "../bin/*",^
          "../icon.png",^
          "../../../README.md",^
          "../../../CHANGELOG.md",^
          "../../../LICENSE"^
    -DestinationPath "../bin/mirage-experimental.zip"