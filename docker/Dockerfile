# Dummy since Steam doesn't support arm64
FROM --platform=$BUILDPLATFORM scratch AS steam-arm64
COPY docker/steamclient.not-supported /home/steam/steamcmd/linux64/

# Download Steam client
FROM --platform=$BUILDPLATFORM cm2network/steamcmd:latest AS steam-amd64

# Select steam base image based on target architecture
FROM --platform=$BUILDPLATFORM steam-${TARGETARCH} AS steam
ARG TARGETARCH

# Build app
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
ARG TARGETARCH

RUN apk add --update --no-cache bash

SHELL ["/bin/bash", "-c"]

RUN echo linux-${TARGETARCH/amd/x} > ~/.RuntimeIdentifier

WORKDIR /app

COPY . ./

RUN dotnet publish -r $(cat ~/.RuntimeIdentifier) -c Release --no-self-contained

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0

WORKDIR /app

COPY --from=steam /home/steam/steamcmd/linux64/steamclient.* /root/.steam/sdk64/
COPY --from=build /app/out-linux-* /app/
COPY docker/setup.py /app/setup.py
COPY docker/pyproject.toml docker/poetry.lock /app/

# Install dependencies
RUN apt-get update && \
    apt-get install -y python3 curl s3cmd && \
    curl -sSL https://install.python-poetry.org | python3 - && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

ENV PATH="/root/.local/bin:$PATH"

WORKDIR /app
RUN poetry install --no-dev --no-root
ENTRYPOINT ["poetry", "run", "python", "/app/setup.py"]
