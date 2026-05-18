FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["src/ClinicalAgent.Api/ClinicalAgent.Api.csproj", "src/ClinicalAgent.Api/"]
COPY ["src/ClinicalAgent.Plugins/ClinicalAgent.Plugins.csproj", "src/ClinicalAgent.Plugins/"]
COPY ["src/ClinicalAgent.Core/ClinicalAgent.Core.csproj", "src/ClinicalAgent.Core/"]
RUN dotnet restore "src/ClinicalAgent.Api/ClinicalAgent.Api.csproj"

COPY . .
WORKDIR "/src/src/ClinicalAgent.Api"
RUN dotnet build "ClinicalAgent.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "ClinicalAgent.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_RUNNING_IN_CONTAINER=true

ENTRYPOINT ["dotnet", "ClinicalAgent.Api.dll"]
