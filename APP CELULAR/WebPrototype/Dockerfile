FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY WebPrototype.csproj ./
RUN dotnet restore WebPrototype.csproj

COPY . ./
RUN dotnet publish WebPrototype.csproj --configuration Release --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
EXPOSE 10000
ENTRYPOINT ["sh", "-c", "dotnet WebPrototype.dll --urls http://0.0.0.0:${PORT:-10000}"]
