# syntax=docker/dockerfile:1
#
# The .NET API with a pinned Terraform CLI. Build from the repo root, since it needs catalog/ as well as api/:
#   docker build -f api/Dockerfile .                    the API (the default, last stage)
#   docker build -f api/Dockerfile --target migrate .   a one-shot image that applies the EF Core migrations

ARG DOTNET_VERSION=10.0

# ── Terraform: a pinned release, checked against HashiCorp's published SHA-256 sums ──
FROM alpine:3.24.4 AS terraform
ARG TARGETARCH
# Keep in step with terraform_version in .github/workflows/ci.yml.
ARG TERRAFORM_VERSION=1.17.1
ARG TERRAFORM_SHA256_AMD64=4638628058e71a55212b1c563712eac03b902128f40740f3dc8174df12135061
ARG TERRAFORM_SHA256_ARM64=988e950a1c23058093b0e66ab2d5fb53fc18345ecfbd5871acf51dbac9325edd
RUN set -eu; \
    case "${TARGETARCH}" in \
      amd64) sha="${TERRAFORM_SHA256_AMD64}" ;; \
      arm64) sha="${TERRAFORM_SHA256_ARM64}" ;; \
      *) echo "No Terraform checksum for ${TARGETARCH}" >&2; exit 1 ;; \
    esac; \
    zip="terraform_${TERRAFORM_VERSION}_linux_${TARGETARCH}.zip"; \
    wget -q "https://releases.hashicorp.com/terraform/${TERRAFORM_VERSION}/${zip}"; \
    echo "${sha}  ${zip}" | sha256sum -c -; \
    unzip -q "${zip}" terraform -d /usr/local/bin; \
    /usr/local/bin/terraform version

# ── Build and publish the API ──
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src/api
COPY api/Directory.Build.props api/Directory.Packages.props api/ResourceProvisioner.slnx ./
COPY api/.config ./.config
COPY api/src/ResourceProvisioner.Api/ResourceProvisioner.Api.csproj src/ResourceProvisioner.Api/
RUN dotnet restore src/ResourceProvisioner.Api/ResourceProvisioner.Api.csproj
COPY api/src ./src
RUN dotnet publish src/ResourceProvisioner.Api/ResourceProvisioner.Api.csproj -c Release -o /out/app --no-restore

# ── EF Core migrations bundle: migrations run as their own step, never at API startup ──
FROM build AS bundle
RUN dotnet tool restore \
 && dotnet ef migrations bundle --project src/ResourceProvisioner.Api --configuration Release -o /out/efbundle

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS migrate
COPY --from=bundle /out/efbundle /efbundle
USER app
# Reads ConnectionStrings__DefaultConnection, like the API.
ENTRYPOINT ["/efbundle"]

# ── The API ──
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS api
WORKDIR /app
COPY --from=terraform /usr/local/bin/terraform /usr/local/bin/terraform
COPY --from=build /out/app ./
COPY catalog ./catalog
# Per-request workspaces hold Terraform state, so they must live on a volume mounted here.
RUN mkdir -p /data/workspaces && chown app:app /data/workspaces
ENV Catalog__Root=/app/catalog \
    Workspaces__Root=/data/workspaces \
    Runner__TerraformPath=/usr/local/bin/terraform
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "ResourceProvisioner.Api.dll"]
