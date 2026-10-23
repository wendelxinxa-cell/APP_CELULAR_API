#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

config_file="Client/wwwroot/supabase-config.js"
mkdir -p "$(dirname "$config_file")"
rm -f "$config_file"
cleanup() { rm -f "$config_file"; }
trap cleanup EXIT

node --input-type=module <<'NODE'
import { readFileSync, writeFileSync } from 'node:fs';

const settings = JSON.parse(readFileSync('appsettings.json', 'utf8'));
const supabase = settings.Supabase ?? {};
if (!process.env.VERCEL_URL) {
  throw new Error('VERCEL_URL ausente: API requests precisam ficar no mesmo domínio para usar os rewrites.');
}
const config = {
  url: process.env.Supabase__Url || supabase.Url,
  publishableKey: process.env.Supabase__PublishableKey || supabase.PublishableKey,
  bucket: process.env.Supabase__Bucket || supabase.Bucket || 'chat-media',
  empresaId: process.env.Supabase__EmpresaId || supabase.EmpresaId || 1,
  apiBaseUrl: `https://${process.env.VERCEL_URL}`
};

if (!config.url || !config.publishableKey || !config.empresaId) {
  throw new Error('Configure Supabase__Url, Supabase__PublishableKey e Supabase__EmpresaId no Vercel.');
}

writeFileSync('Client/wwwroot/supabase-config.js',
  `window.chatcatSupabaseConfig = ${JSON.stringify(config)};\n`,
  { encoding: 'utf8', mode: 0o644 });
NODE

if ! command -v dotnet >/dev/null 2>&1 || ! dotnet --list-sdks | grep '^8\.' >/dev/null; then
  sdk_dir="/tmp/catchat-dotnet-sdk"
  curl --fail --silent --show-error --location https://dot.net/v1/dotnet-install.sh --output /tmp/catchat-dotnet-install.sh
  bash /tmp/catchat-dotnet-install.sh --channel 8.0 --install-dir "$sdk_dir"
  export PATH="$sdk_dir:$PATH"
fi

dotnet publish Client/WebPrototype.Client.csproj \
  --configuration Release \
  --output Client/publish \
  --nologo \
  /p:UseAppHost=false
