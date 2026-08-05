FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/AetherChannel.Contracts/ AetherChannel.Contracts/
COPY src/AetherChannel.Server/ AetherChannel.Server/
RUN dotnet publish AetherChannel.Server/AetherChannel.Server.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000
ENTRYPOINT ["dotnet", "AetherChannel.Server.dll"]
