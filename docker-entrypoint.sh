#!/bin/sh
set -e

# Valor por defeito se não vier de env/compose
API_URL="${API_URL:-https://api.example.org}"

echo "Running Bibliotheca Frontend"

CONFIG=/usr/share/nginx/html/config.js

if [ -f "$CONFIG" ]; then
  # Substitui o placeholder pelo valor de runtime (escapa / para o sed)
  SAFE_API_URL=$(printf '%s\n' "$API_URL" | sed 's/[\/&]/\\&/g')
  sed -i "s/__API_URL__/${SAFE_API_URL}/g" "$CONFIG"
fi

exec "$@"