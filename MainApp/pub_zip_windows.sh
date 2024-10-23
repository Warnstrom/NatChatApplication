#!/bin/bash

# .exe build command
#dotnet publish -r win-x64 -c Release --self-contained true /p:PublishSingleFile=true /p:PublishReadyToRun=true /p:EnableCompressionInSingleFile=true

# contained zipped version
dotnet publish -c Release -r win-x64  -o ./bin/Release/publish_windows-x64

# Navigate to the output directory
cd ./bin/Release/publish_windows-x64

# Run the zip command to compress the contents
zip -r ../publish_windows.zip *