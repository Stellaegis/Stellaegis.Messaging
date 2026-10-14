# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] — 2026-10-14

First release as a standalone library. The README's *Migrating from the Stellaegis framework* lists
what changes for code that used the framework's messaging module.

### Added

- `IEmailSender.SendAsync(Space, EmailMessage)`: a message is sent with the configuration of a
  compartment, or of the shared space.
- `AddStellaegisEmailConfiguration`: the configuration of the shared space read from a configuration
  section, each field from whichever configuration source provides it. `InheritSharedSpace`, off by
  default, lets a compartment without configuration use the shared one.
- `Stellaegis.Messaging.Secrets`: the configuration of each space in a secrets vault, at
  `messaging.{compartment}` or `messaging._shared`, read through a bounded cache.
  `IMessagingConfigCache.Invalidate` evicts what was read from a path. `IMessagingConfigStore` on the
  vault masks secret fields on read, keeps them on a masked save, and evicts what it saves or deletes.
- The sender-address policy of a transport: domain-authorized, mailbox-bound or unrestricted, with a
  mismatch degraded to the transport's default sender or rejected.
- The outbox: at-least-once delivery with leases, exponential retries, dead letters, retention and
  purge, an idempotency key per space, and an in-memory store. `AddStellaegisLoggingEmail` for
  development.
- `Stellaegis.Messaging.MailKit`: the SMTP transport, with Basic or client-certificate authentication
  and an explicit envelope sender.
- `Stellaegis.Messaging.PostgreSQL`: a durable outbox store, messages encrypted at rest with pgcrypto,
  with two key slots for rotation. Requires PostgreSQL 15 or later.

[1.0.0]: https://github.com/Stellaegis/Stellaegis.Messaging/releases/tag/v1.0.0
