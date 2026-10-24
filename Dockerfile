FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
ARG SERVICE=Booking.QueryApi
WORKDIR /src
COPY backend/ ./backend/
COPY Directory.Build.props ./
RUN dotnet restore backend/${SERVICE}/${SERVICE}.csproj
RUN dotnet publish backend/${SERVICE}/${SERVICE}.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false
RUN printf '#!/bin/sh\nexec dotnet /app/%s.dll "$@"\n' "$SERVICE" > /app/entrypoint.sh && chmod 755 /app/entrypoint.sh

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=build /app/entrypoint.sh /app/entrypoint.sh
ENV ASPNETCORE_HTTP_PORTS=8080 DOTNET_EnableDiagnostics=0
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["/app/entrypoint.sh"]
