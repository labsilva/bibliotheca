FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /app/
COPY . .
RUN dotnet build src/Bibliotheca.Backend/Bibliotheca.Backend.csproj -c Release -o output

FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS runtime
# The local Playwright Node driver needs libatomic, including with a remote browser.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libatomic1 \
    && rm -rf /var/lib/apt/lists/*
LABEL pt.labsoft.bibliotheca.image.title="bibliotheca-api"
LABEL pt.labsoft.bibliotheca.image.description="API Backend for Bibliotheca App"
LABEL pt.labsoft.bibliotheca.image.version="1.0.0"
LABEL pt.labsoft.bibliotheca.image.authors="Luis Silva"
LABEL pt.labsoft.bibliotheca.image.url="https://github.com/labsilva"
LABEL pt.labsoft.bibliotheca.image.source="https://github.com/labsilva/bibliotheca"
LABEL pt.labsoft.bibliotheca.image.documentation="https://github.com/labsilva/bibliotheca"
#LABEL pt.labsoft.bibliotheca.image.licenses="MIT"
#LABEL pt.labsoft.bibliotheca.image.revision="commit_sha"
WORKDIR /app/
COPY --from=build /app/output .
RUN mkdir -p /app/data /app/temp && chown -R app:app /app/data /app/temp
ENV ASPNETCORE_HTTP_PORTS=5500
USER app
ENTRYPOINT ["/app/Bibliotheca.Backend"]
