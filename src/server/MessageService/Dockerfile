# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["MessageService.csproj", "./"]
RUN dotnet restore "MessageService.csproj"

# Copy everything else and build
COPY . .
RUN dotnet publish "MessageService.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Create logs directory
RUN mkdir -p logs

# Expose port
EXPOSE 5002

# Set environment
ENV ASPNETCORE_URLS=http://+:5002
ENV ASPNETCORE_ENVIRONMENT=Production

# Copy published app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "MessageService.dll"]
