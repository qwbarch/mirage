@echo off

call ./build.bat
powershell Compress-Archive^
    -Force^
    -Path "../bin/*",^
          "../manifest-experimental.json",^
          "../icon.png",^
          "../../../README.md",^
          "../../../CHANGELOG.md",^
          "../../../LICENSE"^
    -DestinationPath "../bin/mirage-experimental.zip"