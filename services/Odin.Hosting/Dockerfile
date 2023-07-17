FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["lib/Odin.Core/Odin.Core.csproj", "lib/Odin.Core/"]
COPY ["lib/Odin.Core.Storage/Odin.Core.Storage.csproj", "lib/Odin.Core.Storage/"]
COPY ["lib/Odin.Core.Cryptography/Odin.Core.Cryptography.csproj", "lib/Odin.Core.Cryptography/"]
COPY ["services/Odin.Core.Services/Odin.Core.Services.csproj", "services/Odin.Core.Services/"]
COPY ["services/Odin.Hosting/Odin.Hosting.csproj", "services/Odin.Hosting/"]
COPY ["services/Odin.Core.Services.Background/Odin.Core.Services.Background.csproj", "services/Odin.Core.Services.Background/"]
RUN dotnet restore "services/Odin.Hosting/Odin.Hosting.csproj"
COPY . .

RUN dotnet build --warnaserror "services/Odin.Hosting/Odin.Hosting.csproj" -c Release -o /app/build

FROM build AS publish
#RUN dotnet publish "Odin.Hosting/Odin.Hosting.csproj" -c Release -o /app/publish
RUN dotnet publish "services/Odin.Hosting/Odin.Hosting.csproj" /p:RuntimeIdentifier=linux-x64 /p:IncludeNativeLibrariesForSelfExtract=true /p:UseAppHost=true --self-contained true -r linux-x64 -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Odin.Hosting.dll"]