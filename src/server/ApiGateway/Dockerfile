# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["ApiGateway.csproj", "./"]
RUN dotnet restore "ApiGateway.csproj"

# Copy everything else and build
COPY . .
RUN dotnet publish "ApiGateway.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Create logs directory
RUN mkdir -p logs

# Expose port
EXPOSE 5000

# Set environment
ENV ASPNETCORE_URLS=http://+:5000
ENV ASPNETCORE_ENVIRONMENT=Production

# Copy published app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "ApiGateway.dll"]
