# syntax=docker/dockerfile:1.7
#
# C4 GuestPass – obraz produkcyjny (Linux, .NET 8).
# Kontekst budowania = katalog główny repozytorium:
#
#   docker build -f deploy/Dockerfile -t c4guestpass .                                  # tylko Mock C4 (demo)
#   docker build -f deploy/Dockerfile --build-arg C4_SDK_VERSION=2024 -t c4guestpass .  # Simple Client SDK 2024
#   docker build -f deploy/Dockerfile --build-arg C4_SDK_VERSION=2026 -t c4guestpass .  # Simple Client SDK 2026
#
# Jeśli feed Gamanet (nugets.c4portal.com) wymaga logowania, przekaż nuget.config z poświadczeniami
# jako sekret BuildKit (nie trafi do żadnej warstwy obrazu):
#
#   docker build -f deploy/Dockerfile --build-arg C4_SDK_VERSION=2024 \
#     --secret id=nuget_config,src=$HOME/.nuget/NuGet/NuGet.Config -t c4guestpass .

ARG DOTNET_VERSION=8.0

# ---------------------------------------------------------------- build
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build

# None | 2024 | 2026  -> -p:C4SdkVersion (patrz C4GuestPass.Web.csproj)
ARG C4_SDK_VERSION=None
# Opcjonalnie dokładna wersja pakietów Gamanet, np. 2024.0.0.512 (puste = najnowsza z danej linii)
ARG C4_SDK_PACKAGE_VERSION=
ARG CONFIGURATION=Release

WORKDIR /src

# Najpierw sam projekt -> warstwa z restore jest cache'owana, dopóki nie zmienią się zależności.
COPY src/C4GuestPass.Web/C4GuestPass.Web.csproj src/C4GuestPass.Web/

RUN --mount=type=secret,id=nuget_config,target=/run/secrets/nuget.config,required=false \
    set -eu; \
    PROPS="-p:C4SdkVersion=${C4_SDK_VERSION}"; \
    if [ -n "${C4_SDK_PACKAGE_VERSION}" ]; then PROPS="$PROPS -p:C4SdkPackageVersion=${C4_SDK_PACKAGE_VERSION}"; fi; \
    CFG=""; \
    if [ -s /run/secrets/nuget.config ]; then CFG="--configfile /run/secrets/nuget.config"; fi; \
    dotnet restore src/C4GuestPass.Web/C4GuestPass.Web.csproj $PROPS $CFG

COPY src/ src/

# appsettings.Development.json (hasło demo, SeedDemoData=true) usuwamy – nie ma prawa trafić do obrazu.
RUN set -eu; \
    PROPS="-p:C4SdkVersion=${C4_SDK_VERSION}"; \
    if [ -n "${C4_SDK_PACKAGE_VERSION}" ]; then PROPS="$PROPS -p:C4SdkPackageVersion=${C4_SDK_PACKAGE_VERSION}"; fi; \
    dotnet publish src/C4GuestPass.Web/C4GuestPass.Web.csproj \
        -c "${CONFIGURATION}" --no-restore -o /app/publish \
        -p:UseAppHost=false $PROPS; \
    rm -f /app/publish/appsettings.Development.json

# ---------------------------------------------------------------- runtime
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS final

ARG C4_SDK_VERSION=None
LABEL org.opencontainers.image.title="C4 GuestPass" \
      org.opencontainers.image.description="Zaproszenia gości z kodem QR dla Gamanet C4" \
      c4guestpass.c4-sdk-version="${C4_SDK_VERSION}"

WORKDIR /app

# Ścieżki względne z konfiguracji (GuestPass:DatabasePath=data/guestpass.db, Mail:PickupDirectory=data/mail)
# liczone są od katalogu roboczego -> /app/data. Katalog musi należeć do użytkownika bez uprawnień root.
# /home/app/.aspnet/DataProtection-Keys – klucze szyfrujące ciasteczka logowania (bez wolumenu restart
# kontenera wylogowuje wszystkich użytkowników).
RUN mkdir -p /app/data /home/app/.aspnet/DataProtection-Keys \
    && chown -R ${APP_UID}:${APP_UID} /app/data /home/app/.aspnet

COPY --from=build --chown=root:root /app/publish/ ./

# Obraz aspnet:8.0 ustawia ASPNETCORE_HTTP_PORTS=8080 – zerujemy, żeby ASPNETCORE_URLS było jedynym źródłem
# (bez ostrzeżenia "Overriding HTTP_PORTS").
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS= \
    ASPNETCORE_URLS=http://+:8080 \
    DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    TZ=Europe/Warsaw

# Użytkownik "app" (UID 1654) jest wbudowany w obrazy .NET 8.
USER ${APP_UID}

EXPOSE 8080
VOLUME ["/app/data", "/home/app/.aspnet/DataProtection-Keys"]

# W obrazie aspnet nie ma curl ani wget, a /api/health wymaga zalogowania – sprawdzamy więc tylko,
# czy Kestrel przyjmuje połączenia TCP (wbudowany /dev/tcp basha, bez dodatkowych pakietów).
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080"]

ENTRYPOINT ["dotnet", "C4GuestPass.Web.dll"]
