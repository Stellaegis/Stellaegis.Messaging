# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

The module declares itself to the application that assembles it, as every module of the Stellaegis
family does from `Stellaegis.Primitives` 4.0, and its vault paths take the shape that
`Stellaegis.Secrets` 4.0.0 imposes. The four packages move together to 4.0.0, skipping 2 and 3, to share
the major version of the Stellaegis family.

### Migrating from 1.x

- Declare your application's frame with at least one compartment,
  `AddStellaegisFrame(ApplicationFrame.SingleCompartment(owner))` or `ManyCompartments()`. An
  application without compartments, which sent under `Space.Shared`, declares a single compartment named
  after the organization that owns its data, and sends under that compartment.
- `"Stellaegis:Messaging:InheritSharedSpace": true` becomes
  `AddStellaegisModuleParameter<MessagingModule>(MessagingModule.InheritSharedSpace)`. The key is no
  longer read: left in a configuration file, it is ignored, and a compartment without a configuration of
  its own writes its messages to the log instead of sending them. An application configured by a section
  that sends under a compartment turns the parameter on.
- Rename the secrets in the vault: `messaging.{compartment}` becomes
  `{compartment}.messaging.configuration`, and `messaging._shared` becomes
  `_shared.messaging.configuration`. A secret left under its 1.x path is not found: its space has no
  relay, and its messages are written to the log.
- `AddStellaegisEmail(configuration, sectionName)` becomes `AddStellaegisEmail(configuration)`.
- An existing `msg` schema may keep `msg._schema_migrations` and `msg._seed_migrations`: nothing reads
  them, and they can be dropped.

### Added

- `MessagingModule`, the identity of the module, named `messaging`. `AddStellaegisEmail` declares it with
  compartments, through `Stellaegis.Primitives.DependencyInjection`.
- `MessagingModule.InheritSharedSpace`, a capability of the module: the application turns it on as a
  parameter, and both configuration sources read it from the module's declaration.
- The host fails to start when the engine has no source of configuration or no transport, naming the
  registration to add. The first send used to fail on an unresolved service, and the outbox worker to fail
  at each of its cycles.

### Changed

- **Breaking.** The validation of `Stellaegis.Primitives` refuses the messaging module in an application
  whose frame has no compartment, and refuses a module declared twice: `AddStellaegisEmail` is called
  once.
- **Breaking.** The inheritance of the shared space is a parameter of the module, written in code. The
  configuration key `Stellaegis:Messaging:InheritSharedSpace` is no longer read.
- **Breaking.** A vault path has the shape `{space}.messaging.configuration`, from
  `SecretPath.For<MessagingModule>` of `Stellaegis.Secrets` 4.0.0.
- `Stellaegis.Messaging` depends on `Stellaegis.Primitives` 4.3.0 and
  `Stellaegis.Primitives.DependencyInjection` 4.3.0, `Stellaegis.Messaging.PostgreSQL` on
  `Stellaegis.Primitives.Data` 4.3.0, and `Stellaegis.Messaging.Secrets` on `Stellaegis.Secrets` 4.0.0.
- The schema script leaves out `msg._schema_migrations` and `msg._seed_migrations`, which nothing writes.
  It notes that creating the pgcrypto extension needs a database administrator, and why the outbox table
  has no row-level security rule.

### Removed

- **Breaking.** `MessagingOptions`, which held only `InheritSharedSpace`, and the `sectionName` parameter
  of `AddStellaegisEmail`.

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
