-- ============================================================================
-- Stellaegis Messaging — Reset script for schema msg (outbox)
-- ============================================================================
-- WARNING: deletes every message of the outbox, pending ones included. Irreversible.
-- For development only.
-- Usage: psql -d <database> -f reset-messaging.sql
-- ============================================================================

begin;

delete from msg.outbox;

commit;
