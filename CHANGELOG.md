# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [5.0.0] — 2026-10-21

Messaging follows Primitives 5.0.0: the module declares everything the frame is read against — the shared space
it accepts, the capability that supposes one, its compatibility with an isolation by owner, its database —, a
configuration section is read against the frame rather than through a parameter, what the sender refuses carries
a code, the options are validated when the host starts, and a compartment that leaves is purged. The four packages
move together to 5.0.0, the major version of the family.

### Migrating from 4.0.0

In the order a host follows it:

1. The packages reference `Stellaegis.Primitives` 5.0.0: `Stellaegis.Messaging` takes
   `Stellaegis.Primitives.Hosting` in place of `Stellaegis.Primitives.DependencyInjection`, `.PostgreSQL` takes
   `.Data` 5.0.0, `.Secrets` takes `Stellaegis.Secrets` 5.0.0 and `Stellaegis.Caching` 5.0.0. The namespace
   `Stellaegis.Primitives.DependencyInjection` becomes `Stellaegis.Primitives.Hosting`.
2. The extension methods of each package live in its namespace: add `using Stellaegis.Messaging.Secrets;`,
   `using Stellaegis.Messaging.MailKit;` or `using Stellaegis.Messaging.PostgreSQL;` where the registrations are
   written.
3. An application with a single compartment configured by a section removes
   `AddStellaegisModuleParameter<MessagingModule>(MessagingModule.InheritSharedSpace)`: the section is read as the
   configuration of that compartment. An application with several compartments that keeps the parameter declares a
   shared space in its frame, `ApplicationFrame.ManyCompartments().WithSharedSpace()`; without one, the startup
   fails. Its section describes the shared space, and a compartment may have its own under
   `Stellaegis:Messaging:Compartments:{compartment}`.
4. `AddStellaegisEmailConfiguration(configuration, sectionName)` names the parent section, `Stellaegis:Messaging`
   by default, which holds `Configuration` and `Compartments`. A host that passed the default changes nothing.
5. A host with the PostgreSQL outbox calls `AddStellaegisEmailPostgreSql(configuration)` in place of both
   `AddStellaegisEmail` and `AddStellaegisMessagingPostgreSql`, and follows with `AddStellaegisDatabaseVerification()`
   of `Stellaegis.Primitives.Data`, or `app.Services.VerifyStellaegisDatabasesAsync()` on an ASP.NET Core host.
6. The outbox is installed by `DatabaseLifecycle.InstallAsync([MessagingDatabase.Value], frame, openAsAdmin,
   appRole)` rather than by running `docs/sql/stellaegis_messaging.sql`, which leaves the repository. An outbox
   created by the 4.0.0 script keeps its table but is not recorded as installed: let it drain, drop the `msg`
   schema and install again — a dead letter to keep is sent again first. The application connects under the role
   the installation granted.
7. `Stellaegis:Messaging:Database:Passphrase` becomes `Stellaegis:Messaging:Database:Keys:1`: the passphrases are
   the `KeySlots` of Primitives, and the simple form is gone. A `Passphrase` key left in a file is ignored, and the
   host refuses to start, naming `Keys:1`.
8. A caller catching `InvalidOperationException` from `SendAsync` catches `UserFacingException`, and reads its
   `Error.Code` among `MessagingErrors`. `EmailRejectedException` is one, with the code `Email.SenderRejected`.
9. `AddStellaegisEmailOutbox` is gone: the outbox options, `Stellaegis:Messaging:Outbox`, are read by
   `AddStellaegisEmail` and validated at startup; set one from code with `PostConfigure<MessagingOutboxOptions>`.

### Added

- `IMessageOutboxPurge.PurgeCompartmentAsync(compartment, actor)`: every message of a compartment deleted, pending
  ones included, replayable, the actor logged. Implemented by both stores, registered beside `IMessageOutbox`.
- `MessagingDatabase.Value`, the declaration of the outbox's database for `Stellaegis.Primitives.Data`: the
  schema `msg`, the script `0001_initial.sql` embedded, `pgcrypto` required. `AddStellaegisEmailPostgreSql`
  registers it for the verification at startup.
- `MessagingErrors`, the codes of what the sender refuses: `Email.SenderRejected`, `Email.TransportUnknown`,
  `Email.FromRequired`, `Email.TransportMisconfigured`.
- `AddStellaegisEmailServices`, the engine without a store, which the registration of a store builds on.
- `Stellaegis:Messaging:Compartments:{compartment}`: the configuration of one compartment in a section, in an
  application with several.

### Changed

- **Breaking.** The module is declared accepting the shared space, with `inherit_shared_space` as a capability
  that supposes one, and with `owner_isolation` as a compatibility declared from the core: no message of the outbox
  belongs to an actor. An application whose frame declares `WithOwnerIsolation()` now accepts the module.
- **Breaking.** A configuration section is read against the frame: the configuration of the single compartment in
  an application with one, of the shared space in an application with several. The parameter no longer applies to
  the single compartment.
- **Breaking.** `EmailRejectedException` is a `UserFacingException`; the unknown transport, the missing `From` and
  the transport that cannot apply its policy are `UserFacingException` too, each with its code, in place of
  `InvalidOperationException`.
- **Breaking.** `AddStellaegisMessagingPostgreSql` becomes `AddStellaegisEmailPostgreSql`, which registers the
  engine with the durable store in place of `AddStellaegisEmail`, with `TryAdd` rather than by removing the
  in-memory registration: a host calls one of the two.
- **Breaking.** `MessagingDatabaseOptions` holds its passphrases as the `KeySlots` of Primitives: `Passphrase` is
  removed, `Keys` and `ActiveKeySlot` stay.
- **Breaking.** The schema script leaves out `create schema` and `create extension`, which the installation does,
  and replaces the dead-letter index with an index on the compartment, which the purge uses too.
- **Breaking.** The extension classes of `.Secrets` and `.MailKit` are `ServiceCollectionExtensions` in their
  package namespaces, as `.PostgreSQL`'s was.
- The options of every package implement `IValidatableOptions` and are registered by `AddStellaegisOptions`:
  `MessagingOutboxOptions`, `MessagingDatabaseOptions`, `VaultConfigurationOptions`. A setting that cannot work
  fails the host at startup, naming it; the PostgreSQL store refuses the same options when built by hand.
- The cache of vault configurations is keyed by the space rather than by the path of its secret.
- The SMTP transport loads a client certificate once per distinct PFX and keeps it, rather than at every send.
- The development senders write to the log only: the console box is gone, the body is a property of the log entry.
- `Stellaegis.Messaging.Secrets` catches `SecretNotFoundException` of `Stellaegis.Secrets` 5.0.0, no longer
  `KeyNotFoundException`, and reads the blob of a space through one reader shared by the resolver and the store.
- The README documents several workers over one durable outbox, and the purge of a compartment.

### Removed

- **Breaking.** `AddStellaegisEmailOutbox`, `AddStellaegisMessagingPostgreSql`, `MessagingDatabaseOptions.Passphrase`,
  `docs/sql/stellaegis_messaging.sql` and `docs/sql/reset-messaging.sql`.

## [4.0.0] — 2026-10-20

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

[4.0.0]: https://github.com/Stellaegis/Stellaegis.Messaging/releases/tag/v4.0.0
[1.0.0]: https://github.com/Stellaegis/Stellaegis.Messaging/releases/tag/v1.0.0
