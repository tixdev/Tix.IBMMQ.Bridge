FROM mcr.microsoft.com/dotnet/sdk:8.0 AS final

# Copia tutto il progetto (inclusi .sln e .csproj e certificati) in /src
WORKDIR /src
COPY . .

# Istalla certificati
RUN apt-get update
RUN apt install -y ca-certificates

ADD certs/ca-certificates.crt /usr/local/share/ca-certificates/ca-certificates.crt
RUN chmod 644 /usr/local/share/ca-certificates/ca-certificates.crt && update-ca-certificates 

# Publish dell'applicazione in /app
RUN dotnet publish Tix.IBMMQ.Bridge.sln -c Release -o /app
WORKDIR /app

ENTRYPOINT ["dotnet", "Tix.IBMMQ.Bridge.dll"]
