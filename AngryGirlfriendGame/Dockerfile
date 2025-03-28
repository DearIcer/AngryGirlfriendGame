FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["AngryGirlfriendGame/AngryGirlfriendGame.csproj", "AngryGirlfriendGame/"]
RUN dotnet restore "AngryGirlfriendGame/AngryGirlfriendGame.csproj"
COPY . .
WORKDIR "/src/AngryGirlfriendGame"
RUN dotnet build "AngryGirlfriendGame.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "AngryGirlfriendGame.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "AngryGirlfriendGame.dll"]
