FROM mcr.microsoft.com/dotnet/core/sdk:3.1.402-alpine3.12
WORKDIR /ubiety

deps:
    COPY src/Ubiety.Build.Tool/Ubiety.Build.Tool.csproj src/Ubiety.Build.Tool/
    COPY .config .config
    RUN dotnet restore src/Ubiety.Build.Tool
    RUN dotnet tool restore
    SAVE IMAGE

build:
    FROM +deps
    COPY src src
    COPY build build
    COPY build.sh ./build.sh
    RUN dos2unix ./build.sh
    RUN ./build.sh
