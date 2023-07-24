FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /app

COPY lib/Odin.Core/Odin.Core.csproj lib/Odin.Core/
COPY lib/Odin.Core.Storage/Odin.Core.Storage.csproj lib/Odin.Core.Storage/
COPY lib/Odin.Core.Cryptography/Odin.Core.Cryptography.csproj lib/Odin.Core.Cryptography/
COPY services/Odin.Core.Services/Odin.Core.Services.csproj services/Odin.Core.Services/
COPY services/Odin.Hosting/Odin.Hosting.csproj services/Odin.Hosting/
COPY services/Odin.Core.Services.Background/Odin.Core.Services.Background.csproj services/Odin.Core.Services.Background/
RUN dotnet restore services/Odin.Hosting/Odin.Hosting.csproj

COPY . ./
RUN dotnet publish --warnaserror "services/Odin.Hosting/Odin.Hosting.csproj" -c Release -o out

FROM mcr.microsoft.com/dotnet/aspnet:6.0
WORKDIR /app
COPY --from=build /app/out .
EXPOSE 80
EXPOSE 443
ENTRYPOINT ["dotnet", "Odin.Hosting.dll"]
