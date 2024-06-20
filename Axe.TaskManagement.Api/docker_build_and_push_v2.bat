@echo off
cd /d %~dp0
@RD /S /Q "./bin/Release"
dotnet publish -c Release -r alpine-x64 --self-contained true /p:PublishTrimmed=true -o ./bin/Release/net6.0/publish
docker build -t docker.cybereye.vn/axe.taskmanagement.api:1.0 -f Dockerfile .
docker push docker.cybereye.vn/axe.taskmanagement.api:1.0
pause