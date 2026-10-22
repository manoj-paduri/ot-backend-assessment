#!/usr/bin/env sh
# Starts a local RabbitMQ (guest/guest) on localhost:5672, management UI on http://localhost:15672
docker compose -f "$(dirname "$0")/docker-compose.yml" up -d
