FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY SistemaEstoqueTI.Api.csproj ./
RUN dotnet restore
COPY . .
RUN dotnet publish SistemaEstoqueTI.Api.csproj -c Release -o /app/publish --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "SistemaEstoqueTI.Api.dll"]
