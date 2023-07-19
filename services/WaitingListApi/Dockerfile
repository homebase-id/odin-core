FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["lib/Odin.Core/Odin.Core.csproj", "lib/Odin.Core/"]
COPY ["lib/Odin.Core.Storage/Odin.Core.Storage.csproj", "lib/Odin.Core.Storage/"]
COPY ["lib/Odin.Core.Cryptography/Odin.Core.Cryptography.csproj", "lib/Odin.Core.Cryptography/"]
COPY ["services/WaitingListApi/WaitingListApi.csproj", "services/WaitingListApi/"]
RUN dotnet restore "services/WaitingListApi/WaitingListApi.csproj"
COPY . .

RUN dotnet build "services/WaitingListApi/WaitingListApi.csproj" -c Release -o /app/build

FROM build AS publish

RUN dotnet publish "services/WaitingListApi/WaitingListApi.csproj" /p:RuntimeIdentifier=linux-x64 /p:IncludeNativeLibrariesForSelfExtract=true /p:UseAppHost=true --self-contained true -r linux-x64 -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "WaitingListApi.dll"]