#!/usr/bin/env sh
# Waits until every submitted wager has been stored, then prints a reconciliation summary.
#   usage: scripts/wait-for-drain.sh [expected=7000] [baseUrl=http://localhost:5000] [baseline=0] [timeoutSeconds=600]
# Exit code 0 only if nothing is missing, queued or dead-lettered.
EXPECTED="${1:-7000}"
BASE_URL="${2:-http://localhost:5000}"
BASELINE="${3:-0}"
TIMEOUT="${4:-600}"
TARGET=$((BASELINE + EXPECTED))
START=$(date +%s)

field() { printf '%s' "$1" | grep -o "\"$2\":[0-9]*" | head -n 1 | cut -d: -f2; }

echo "Waiting for $EXPECTED wagers to be stored (baseline $BASELINE) at $BASE_URL ..."
STORED=0; QUEUED=0; DEAD=0; HAVE_STATS=0

while : ; do
  BODY=$(curl -fsS --max-time 10 "$BASE_URL/api/admin/stats" 2>/dev/null)
  NOW=$(date +%s); ELAPSED=$((NOW - START))
  if [ -n "$BODY" ]; then
    HAVE_STATS=1
    STORED=$(field "$BODY" storedWagers); QUEUED=$(field "$BODY" queuedWagers); DEAD=$(field "$BODY" deadLetteredWagers)
    printf '%6ss  stored %8s  queued %8s  dead-lettered %5s\n' "$ELAPSED" "$STORED" "$QUEUED" "$DEAD"
    if [ "$QUEUED" -eq 0 ] && [ "$STORED" -ge "$TARGET" ]; then break; fi
  else
    printf '%6ss  stats endpoint not reachable yet\n' "$ELAPSED"
  fi
  if [ "$ELAPSED" -gt "$TIMEOUT" ]; then echo "Timed out after ${TIMEOUT}s."; break; fi
  sleep 1
done

[ "$HAVE_STATS" -eq 1 ] || { echo "No statistics could be read."; exit 2; }

THIS_RUN=$((STORED - BASELINE)); MISSING=$((EXPECTED - THIS_RUN))
echo
echo "--- Reconciliation ---------------------------------"
echo "Submitted (expected) : $EXPECTED"
echo "Stored this run      : $THIS_RUN"
echo "Still queued         : $QUEUED"
echo "Dead-lettered        : $DEAD"
echo "Missing              : $MISSING"
echo "Elapsed              : ${ELAPSED}s"

if [ "$MISSING" -eq 0 ] && [ "$DEAD" -eq 0 ] && [ "$QUEUED" -eq 0 ]; then
  echo "RESULT: every submitted wager was stored."; exit 0
fi
echo "RESULT: wagers are missing, queued or dead-lettered - investigate."; exit 1
