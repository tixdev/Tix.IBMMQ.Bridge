FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copia tutto il progetto (inclusi .sln e .csproj)
COPY . .

# Publish dell'applicazione
RUN dotnet publish Tix.IBMMQ.Bridge.sln -c Release -o /app/publish

# Stage finale: usa il runtime ASP.NET Core
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Tix.IBMMQ.Bridge.dll"]
