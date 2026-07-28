FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY MainProjectNumoPart.csproj .
RUN dotnet restore MainProjectNumoPart.csproj
COPY . .
RUN dotnet publish MainProjectNumoPart.csproj -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MainProjectNumoPart.dll"]
