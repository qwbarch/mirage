@echo off

powershell -Command "conda env export --name mirage | Select-String -Pattern '^(?!prefix:)' | ForEach-Object { $_.Line } | Out-File -FilePath ../lib/environment.yml"