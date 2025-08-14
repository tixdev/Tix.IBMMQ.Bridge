FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files
COPY Tix.IBMMQ.Bridge.sln .
COPY Tix.IBMMQ.Bridge/Tix.IBMMQ.Bridge.csproj Tix.IBMMQ.Bridge/

# Restore dependencies
RUN dotnet restore Tix.IBMMQ.Bridge.sln

# Copy the rest of the source code
COPY . .

# Publish the application
WORKDIR /src/Tix.IBMMQ.Bridge
RUN dotnet publish -c Release -o /app/publish

# Final stage
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Tix.IBMMQ.Bridge.dll"]
