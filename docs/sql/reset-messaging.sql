-- ============================================================================
-- Stellaegis — Reset script for schema: msg (Messaging outbox)
-- ============================================================================
-- WARNING: Destroys ALL data in the msg schema. Irreversible.
-- Intended for development/pre-release reset only.
-- Usage: psql -U stellaegis -d stellaegis_messaging -f reset-messaging.sql
-- ============================================================================

BEGIN;

DELETE FROM msg.outbox;

COMMIT;
