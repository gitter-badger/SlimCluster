#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/core/runtime:3.1-buster-slim AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/core/sdk:3.1-buster AS build
WORKDIR /src
COPY . ./
ENV SOLUTION "SlimCluster.sln"
RUN dotnet restore "${SOLUTION}"
RUN dotnet build "${SOLUTION}" -c Release -o /app/build

FROM build AS publish
ARG SERVICE
RUN dotnet publish "Samples/${SERVICE}" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ARG SERVICE
ENV SERVICE_FILE "${SERVICE}.dll"
CMD dotnet $SERVICE_FILE
