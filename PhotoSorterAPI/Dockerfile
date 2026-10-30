# Build context is the repository root (see .github/workflows/docker-publish.yml)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj and restore as distinct layers
COPY PhotoSorterAPI/PhotoSorterAPI.csproj PhotoSorterAPI/
RUN dotnet restore PhotoSorterAPI/PhotoSorterAPI.csproj

# Copy everything else and build
COPY PhotoSorterAPI/ PhotoSorterAPI/
RUN dotnet publish PhotoSorterAPI/PhotoSorterAPI.csproj -c Release -o /app/out --no-restore

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/out .
ENTRYPOINT ["dotnet", "PhotoSorterAPI.dll"]

