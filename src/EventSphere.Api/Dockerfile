FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["src/EventSphere.Domain/EventSphere.Domain.csproj", "src/EventSphere.Domain/"]
COPY ["src/EventSphere.Application/EventSphere.Application.csproj", "src/EventSphere.Application/"]
COPY ["src/EventSphere.Infrastructure/EventSphere.Infrastructure.csproj", "src/EventSphere.Infrastructure/"]
COPY ["src/EventSphere.Api/EventSphere.Api.csproj", "src/EventSphere.Api/"]
RUN dotnet restore "src/EventSphere.Api/EventSphere.Api.csproj"
COPY . .
RUN dotnet publish "src/EventSphere.Api/EventSphere.Api.csproj" -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
RUN addgroup --system eventsphere && adduser --system --ingroup eventsphere eventsphere
COPY --from=build /app .
USER eventsphere
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "EventSphere.Api.dll"]
