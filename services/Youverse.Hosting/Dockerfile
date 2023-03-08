FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["lib/Youverse.Core/Youverse.Core.csproj", "lib/Youverse.Core/"]
COPY ["lib/Youverse.Core.Storage/Youverse.Core.Storage.csproj", "lib/Youverse.Core.Storage/"]
COPY ["lib/Youverse.Core.Cryptography/Youverse.Core.Cryptography.csproj", "lib/Youverse.Core.Cryptography/"]
COPY ["services/Youverse.Core.Services/Youverse.Core.Services.csproj", "services/Youverse.Core.Services/"]
COPY ["services/Youverse.Provisioning/Youverse.Provisioning.csproj", "services/Youverse.Provisioning/"]
COPY ["services/Youverse.Hosting/Youverse.Hosting.csproj", "services/Youverse.Hosting/"]
COPY ["services/Youverse.Core.Services.Background/Youverse.Core.Services.Background.csproj", "services/Youverse.Core.Services.Background/"]
RUN dotnet restore "services/Youverse.Hosting/Youverse.Hosting.csproj"
COPY . .

RUN dotnet build --warnaserror "services/Youverse.Hosting/Youverse.Hosting.csproj" -c Release -o /app/build

FROM build AS publish
#RUN dotnet publish "Youverse.Hosting/Youverse.Hosting.csproj" -c Release -o /app/publish
RUN dotnet publish "services/Youverse.Hosting/Youverse.Hosting.csproj" /p:RuntimeIdentifier=linux-x64 /p:IncludeNativeLibrariesForSelfExtract=true /p:UseAppHost=true --self-contained true -r linux-x64 -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Youverse.Hosting.dll"]