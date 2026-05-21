#!/usr/bin/env bash
set -euo pipefail

SUBJECT="${1:-mailto:admin@alfagestion.com}"

if ! command -v npx >/dev/null 2>&1; then
  echo "No se encontró npx. Instalá Node.js antes de generar las claves VAPID." >&2
  exit 1
fi

echo
echo "Claves VAPID generadas para AlfaCore"
echo "Subject: ${SUBJECT}"
echo
npx web-push generate-vapid-keys
echo
echo "Configurar en producción como variables de entorno:"
echo "PushNotifications__Subject=${SUBJECT}"
echo "PushNotifications__PublicKey=<Public Key generada>"
echo "PushNotifications__PrivateKey=<Private Key generada>"
echo
echo "No pegues la PrivateKey en código fuente ni la envíes al navegador."
