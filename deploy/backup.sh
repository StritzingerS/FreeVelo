#!/bin/sh
# Daily backup of the rider database (a consistent SQLite copy while the server keeps running).
# Keeps the last 14 days in ~/freevelo-backups. Cron (crontab -e):
#   17 4 * * * /home/ubuntu/FreeVelo/deploy/backup.sh
set -e
cd "$(dirname "$0")"
dir="$HOME/freevelo-backups"
mkdir -p "$dir"
volume="$(docker volume ls -q | grep 'freevelo-data$' | head -n 1)"
docker run --rm -v "$volume:/data:ro" -v "$dir:/backup" alpine:3 \
  sh -c "apk add --no-cache sqlite >/dev/null && sqlite3 /data/freevelo.db \".backup /backup/freevelo-$(date +%F).db\""
find "$dir" -name 'freevelo-*.db' -mtime +14 -delete
echo "Backup: $dir/freevelo-$(date +%F).db"
