# Conference Service Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5007

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["ConferenceService.csproj", "./"]
RUN dotnet restore "ConferenceService.csproj"
COPY . .
RUN dotnet publish "ConferenceService.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ConferenceService.dll"]
