FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Logistica.csproj ./
RUN dotnet restore Logistica.csproj
COPY . .
RUN dotnet publish Logistica.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
# Sin root (auditoria_seguridad.md hallazgo 16): la imagen trae el usuario sin privilegios "app". No
# escribe en disco — en producción las fotos van a Cloudinary (Almacenamiento:Proveedor).
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:10000
EXPOSE 10000
ENTRYPOINT ["dotnet", "Logistica.dll"]
