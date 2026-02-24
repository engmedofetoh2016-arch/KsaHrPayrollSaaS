#!/bin/sh
set -eu

cat > /usr/share/nginx/html/runtime-config.js <<EOF
window.__apiBaseUrl = "${API_BASE_URL:-http://localhost:5202}";
EOF

exec nginx -g 'daemon off;'
