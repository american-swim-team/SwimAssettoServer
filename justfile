# Default recipe
default: build

# Configuration paths (server/ is gitignored)
config_source := "~/src/swim-nomad-ops/us/assetto/assetto-srp-public"
server_dir := "server"
config_dest := server_dir + "/cfg"
content_dir := server_dir + "/content"

# Build the project and copy plugins
build:
    dotnet build -c Release
    @just copy-plugins

# Copy enabled plugins to the plugins directory
copy-plugins:
    #!/usr/bin/env bash
    set -euo pipefail
    plugins_dir="AssettoServer/bin/Release/net9.0/plugins"
    mkdir -p "$plugins_dir"
    for plugin in SwimCrashPlugin RandomWeatherPlugin ReportPlugin DiscordAuditPlugin SwimCutupPlugin ReverseProxyPlugin; do
        mkdir -p "$plugins_dir/$plugin"
        cp "$plugin/bin/Release/net9.0/"*.dll "$plugins_dir/$plugin/"
    done

# Publish for local runtime
publish:
    dotnet publish AssettoServer/AssettoServer.csproj -c Release --no-self-contained

# Setup config directory with swim-srp-public config
setup-config:
    mkdir -p {{config_dest}}
    cp {{config_source}}/server_cfg.ini {{config_dest}}/
    cp {{config_source}}/extra_cfg.yml {{config_dest}}/
    cp {{config_source}}/entry_list.ini {{config_dest}}/
    cp {{config_source}}/csp_extra_options.ini {{config_dest}}/
    cp {{config_source}}/data_track_params.ini {{config_dest}}/
    cp {{config_source}}/plugin_*.yml {{config_dest}}/
    mkdir -p {{config_dest}}/cm_content
    cp -r {{config_source}}/cm_content/* {{config_dest}}/cm_content/

# Sync content from S3 (run once, then reuse)
# Uses 1Password CLI to fetch credentials
# Note: srp-021224 has both cars AND tracks with AI splines
sync-content:
    #!/usr/bin/env bash
    set -euo pipefail
    mkdir -p {{content_dir}}
    export AWS_ACCESS_KEY_ID=$(op read "op://Ursi Infrastructure/Linode S3 - Assetto Content/username")
    export AWS_SECRET_ACCESS_KEY=$(op read "op://Ursi Infrastructure/Linode S3 - Assetto Content/credential")
    s3cmd --access_key="$AWS_ACCESS_KEY_ID" --secret_key="$AWS_SECRET_ACCESS_KEY" \
        --host=us-east-1.linodeobjects.com --host-bucket="%(bucket)s.us-east-1.linodeobjects.com" \
        sync s3://swim-assetto-content/srp-021224/content/ {{content_dir}}/

# Run the server (from server/ directory)
run: build
    cd {{server_dir}} && ../AssettoServer/bin/Release/net9.0/AssettoServer

# Clean build artifacts and server files
clean:
    dotnet clean
    rm -rf {{server_dir}}/
