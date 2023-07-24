# here's what to do when we upgrade to dotnet 8:
# https://devblogs.microsoft.com/dotnet/improving-multiplatform-container-support/ 

# https://github.com/dotnet/dotnet-docker/issues/4388
# 

ARG SDK_ARCH
FROM mcr.microsoft.com/dotnet/sdk:6.0-alpine-$SDK_ARCH AS build
WORKDIR /app

COPY lib/Odin.Core/Odin.Core.csproj lib/Odin.Core/
COPY lib/Odin.Core.Storage/Odin.Core.Storage.csproj lib/Odin.Core.Storage/
COPY lib/Odin.Core.Cryptography/Odin.Core.Cryptography.csproj lib/Odin.Core.Cryptography/
COPY services/Odin.Core.Services/Odin.Core.Services.csproj services/Odin.Core.Services/
COPY services/Odin.Hosting/Odin.Hosting.csproj services/Odin.Hosting/
COPY services/Odin.Core.Services.Background/Odin.Core.Services.Background.csproj services/Odin.Core.Services.Background/

ARG RUNTIME_ARCH
RUN dotnet restore -r $RUNTIME_ARCH services/Odin.Hosting/Odin.Hosting.csproj

COPY . ./
ARG RUNTIME_ARCH
RUN dotnet publish -r $RUNTIME_ARCH --warnaserror --self-contained false services/Odin.Hosting/Odin.Hosting.csproj -c Release -o out

ARG SDK_ARCH
FROM mcr.microsoft.com/dotnet/aspnet:6.0-alpine-$SDK_ARCH
WORKDIR /app
COPY --from=build /app/out .
EXPOSE 80
EXPOSE 443
ENTRYPOINT ["dotnet", "Odin.Hosting.dll"]
