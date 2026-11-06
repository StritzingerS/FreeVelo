# FreeVelo server. Build from the repository root:
#   docker build -f src/FreeVelo.Server/Dockerfile -t freevelo-server .
# Multi-arch base images: runs on the Oracle Free ARM VM as well as on x64.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Directory.Build.props src/Directory.Build.targets src/
COPY src/FreeVelo.Shared/ src/FreeVelo.Shared/
COPY src/FreeVelo.Core/ src/FreeVelo.Core/
COPY src/FreeVelo.Server/ src/FreeVelo.Server/
RUN dotnet publish src/FreeVelo.Server/FreeVelo.Server.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# Database and signing key live in a volume, so updates keep the accounts.
ENV Server__DataDirectory=/data \
    ASPNETCORE_HTTP_PORTS=8080
RUN mkdir /data && chown $APP_UID /data
VOLUME /data
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "FreeVelo.Server.dll"]
