FROM node:25-alpine AS frontend-build
WORKDIR /source/web

COPY web/package.json web/pnpm-lock.yaml ./

RUN corepack enable && pnpm install --frozen-lockfile

COPY web/ ./

RUN pnpm build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY global.json Directory.Build.props Directory.Packages.props BannedSymbols.txt ./
COPY dotnet-modulith-baseline.sln ./
COPY src/ src/
COPY --from=frontend-build /source/web/dist/ web/dist/

RUN dotnet restore src/ApiHost/ApiHost.csproj
RUN dotnet publish src/ApiHost/ApiHost.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --system appgroup \
    && useradd --system --gid appgroup --no-create-home appuser

COPY --from=build /app .

RUN chown -R appuser:appgroup /app

USER appuser

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=5s --retries=3 CMD ["curl", "-f", "http://localhost:8080/health"]

ENTRYPOINT ["dotnet", "ApiHost.dll"]
