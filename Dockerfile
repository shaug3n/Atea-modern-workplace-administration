FROM node:22-alpine AS web
WORKDIR /web
COPY src/Web/package*.json ./
RUN npm ci
COPY src/Web ./
ARG VITE_ENTRA_CLIENT_ID
ARG VITE_ENTRA_API_SCOPE
ARG VITE_ENTRA_AUTHORITY
ARG VITE_ENTRA_REDIRECT_URI
ENV VITE_ENTRA_CLIENT_ID=$VITE_ENTRA_CLIENT_ID
ENV VITE_ENTRA_API_SCOPE=$VITE_ENTRA_API_SCOPE
ENV VITE_ENTRA_AUTHORITY=$VITE_ENTRA_AUTHORITY
ENV VITE_ENTRA_REDIRECT_URI=$VITE_ENTRA_REDIRECT_URI
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS api-build
WORKDIR /src
COPY . .
RUN dotnet publish src/Api/Atea.UnifiedWorkplace.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=api-build /app/publish .
COPY --from=web /web/dist ./wwwroot
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Atea.UnifiedWorkplace.Api.dll"]
