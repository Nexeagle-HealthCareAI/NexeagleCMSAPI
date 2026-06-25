FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5002

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["CMSAPI/CMSAPI.csproj",                     "CMSAPI/"]
COPY ["CMSAPI.Application/CMSAPI.Application.csproj", "CMSAPI.Application/"]
COPY ["CMSAPI.Domain/CMSAPI.Domain.csproj",           "CMSAPI.Domain/"]
COPY ["CMSAPI.Data/CMSAPI.Data.csproj",               "CMSAPI.Data/"]
RUN dotnet restore "CMSAPI/CMSAPI.csproj"
COPY . .
WORKDIR "/src/CMSAPI"
RUN dotnet build "CMSAPI.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "CMSAPI.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CMSAPI.dll"]
