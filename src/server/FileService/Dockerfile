# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["FileService.csproj", "./"]
RUN dotnet restore "FileService.csproj"

# Copy everything else and build
COPY . .
RUN dotnet publish "FileService.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Create logs directory
RUN mkdir -p logs

# Expose port
EXPOSE 5003

# Set environment
ENV ASPNETCORE_URLS=http://+:5003
ENV ASPNETCORE_ENVIRONMENT=Production

# Copy published app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "FileService.dll"]
