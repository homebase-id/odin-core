# here's what to do when we upgrade to dotnet 8:
# https://devblogs.microsoft.com/dotnet/improving-multiplatform-container-support/

# https://github.com/dotnet/dotnet-docker/issues/4388
#

FROM mcr.microsoft.com/dotnet/sdk:6.0-alpine AS build
WORKDIR /app

COPY lib/Odin.Core/Odin.Core.csproj lib/Odin.Core/
COPY lib/Odin.Core.Storage/Odin.Core.Storage.csproj lib/Odin.Core.Storage/
COPY lib/Odin.Core.Cryptography/Odin.Core.Cryptography.csproj lib/Odin.Core.Cryptography/
COPY services/Odin.Core.Services/Odin.Core.Services.csproj services/Odin.Core.Services/
COPY services/Odin.Hosting/Odin.Hosting.csproj services/Odin.Hosting/
COPY services/Odin.Core.Services.Background/Odin.Core.Services.Background.csproj services/Odin.Core.Services.Background/

RUN dotnet restore services/Odin.Hosting/Odin.Hosting.csproj

COPY . ./
ARG RUNTIME_ARCH
RUN dotnet publish --warnaserror --self-contained false services/Odin.Hosting/Odin.Hosting.csproj -c Release -o out

FROM mcr.microsoft.com/dotnet/aspnet:6.0-alpine
WORKDIR /app
COPY --from=build /app/out .
EXPOSE 80
EXPOSE 443
ENTRYPOINT ["dotnet", "Odin.Hosting.dll"]
