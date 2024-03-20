@echo off 

cd ../lib
powershell -Command "conda run -n whisper python -m unittest discover -p 'test.py'"
cd ../script