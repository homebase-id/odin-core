FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["dotyoucore-lib/Youverse.Core/Youverse.Core.csproj", "dotyoucore-lib/Youverse.Core/"]
COPY ["dotyoucore-lib/Youverse.Core.Storage/Youverse.Core.Storage.csproj", "dotyoucore-lib/Youverse.Core.Storage/"]
COPY ["dotyoucore-lib/Youverse.Core.Cryptography/Youverse.Core.Cryptography.csproj", "dotyoucore-lib/Youverse.Core.Cryptography/"]
COPY ["dotyoucore/Youverse.Core.Services/Youverse.Core.Services.csproj", "dotyoucore/Youverse.Core.Services/"]
COPY ["dotyoucore/Youverse.Hosting/Youverse.Hosting.csproj", "dotyoucore/Youverse.Hosting/"]
COPY ["dotyoucore/Youverse.Core.Services.Background/Youverse.Core.Services.Background.csproj", "dotyoucore/Youverse.Core.Services.Background/"]
RUN dotnet restore "dotyoucore/Youverse.Hosting/Youverse.Hosting.csproj"
COPY . .

#RUN echo $(ls -al)
#WORKDIR ""/Youverse.Hosting"
RUN dotnet build "dotyoucore/Youverse.Hosting/Youverse.Hosting.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "dotyoucore/Youverse.Hosting/Youverse.Hosting.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Youverse.Hosting.dll"]

# drw-r--r-- 1 root root 1157 Oct 16 16:39 Dockerfile
# drwxr-xr-x 2 root root 4096 Oct 15 22:55 Youverse.Core 
# drwxr-xr-x 2 root root 4096 Oct 15 22:55 Youverse.Core.Cryptography 
# drwxr-xr-x 1 root root 4096 Oct 16 16:34 Youverse.Core.Services 
# drwxr-xr-x 1 root root 4096 Oct 16 16:34 Youverse.Core.Services.Background 
# drwxr-xr-x 2 root root 4096 Oct 15 22:55 Youverse.Core.Storage 
# drwxr-xr-x 1 root root 4096 Oct 16 16:34 Youverse.Hosting 
# drwxr-xr-x 11 root root 4096 Oct 16 16:41 dotyoucore 
# drwxr-xr-x 13 root root 4096 Oct 16 16:41 dotyoucore-lib
