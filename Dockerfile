# ビルドステージ
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# ソース全体をコピー
COPY . .

# Release ビルド
RUN dotnet publish OpsContext.Web/OpsContext.Web.csproj \
    -c Release \
    -o /app/publish \
    --no-self-contained \
    -r linux-x64

# ランタイムステージ
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "OpsContext.Web.dll"]
