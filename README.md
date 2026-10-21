# Stellaegis.Messaging

Stellaegis.Messaging is a .NET library that sends transactional email — a password reset, an
invitation, an invoice notice. It suits an application with a single relay as well as one whose data
is partitioned into compartments, where each compartment can have its own relay, its own credentials
and its own sender addresses. The library reads that configuration from your application's
configuration or from a secrets vault, decides which sender address a relay accepts, and delivers
through an outbox, which retries a message when the relay is unreachable and keeps it for inspection
when every attempt has failed.

The library sends a message whose content you have produced. Finding a template and rendering it is
left to your application and to the template engine of your choice.

The library targets .NET 10 and is released under the MIT license.

## Packages

The library is split so that you only take the dependencies of what you use.

| Package | Contents | Depends on |
| --- | --- | --- |
| `Stellaegis.Messaging` | `EmailMessage` and `IEmailSender`, the configuration of a space and its reading from a configuration section, the sender-address policy, the outbox and its background worker, an in-memory outbox store | `Stellaegis.Primitives`, `Stellaegis.Primitives.Hosting`, Microsoft.Extensions abstractions |
| `Stellaegis.Messaging.Secrets` | The configuration of each space in a secrets vault, read through a cache, and a store to edit it | `Stellaegis.Secrets`, `Stellaegis.Caching` |
| `Stellaegis.Messaging.MailKit` | The SMTP transport, on MailKit: Basic or client-certificate authentication, explicit envelope sender | MailKit |
| `Stellaegis.Messaging.PostgreSQL` | A durable outbox store, messages encrypted at rest with pgcrypto, and the declaration of its database for installation and verification | Npgsql, Dapper, `Stellaegis.Primitives.Data` |

The transport package is named after the library it uses, so that another transport — another SMTP
client, or the HTTP API of an email provider — can be added as a package of its own.

## How a message travels

1. Your code calls `IEmailSender.SendAsync(space, message)`. The space says whose message it is: a
   compartment, or the shared space (see *Spaces* below).
2. The sender reads the configuration of that space, from the source you registered.
3. It selects a transport — a named relay — and applies that transport's sender-address policy: the
   `From` address is kept, replaced, or the message is refused.
4. The message, now final, is written to the outbox. `SendAsync` returns at that point. The message
   has been accepted for delivery; it has not been delivered yet.
5. A background worker, `OutboxProcessor`, claims due messages, reads the transport configuration
   again, and hands each message to the registered `IEmailTransport`.
6. A failed attempt is retried later, with a growing delay. Once the attempt limit is reached, the
   message becomes a dead letter: it stays in the outbox for inspection, and is purged after a while.

Credentials never enter the outbox. The worker reads them from the configuration source at send time,
so a password changed between the two steps is the one used.

## Getting started

The messaging module has compartments, so your application declares a frame with at least one
compartment. The frame is an `ApplicationFrame` of `Stellaegis.Primitives`, written once in code that
every host of your application shares; the README of `Stellaegis.Primitives` describes it, in § *The
application frame*. An application that serves a single organization declares a single compartment,
named after that organization:

```csharp
public static class AppFrame
{
    public static readonly Compartment Owner = Compartment.Parse("acme");

    public static readonly ApplicationFrame Value = ApplicationFrame.SingleCompartment(Owner);
}
```

Then register the frame, the messaging engine, the source of its configuration and a transport. With one
relay, the configuration comes from your application's configuration, read against the frame: in an
application with a single compartment, the section describes that compartment, and nothing else is to turn
on:

```csharp
builder.Services.AddStellaegisFrame(AppFrame.Value);
builder.Services.AddStellaegisEmail(builder.Configuration);
builder.Services.AddStellaegisEmailConfiguration(builder.Configuration);
builder.Services.AddStellaegisMailKitTransport();
```

With a relay per compartment, the configuration comes from a secrets vault, through
`Stellaegis.Messaging.Secrets` and any provider of `Stellaegis.Secrets`:

```csharp
builder.Services.AddStellaegisFrame(AppFrame.Value);   // ApplicationFrame.ManyCompartments(), for instance
builder.Services.AddDatabaseSecretsProvider(builder.Configuration);
builder.Services.AddStellaegisEmail(builder.Configuration);
builder.Services.AddStellaegisEmailVaultConfiguration(builder.Configuration);
builder.Services.AddStellaegisMailKitTransport();
```

The extension methods live in the namespace of their package: `Stellaegis.Messaging`,
`Stellaegis.Messaging.Secrets`, `Stellaegis.Messaging.MailKit`, `Stellaegis.Messaging.PostgreSQL`.

Call `AddStellaegisEmail` once — or `AddStellaegisEmailPostgreSql` in its place, for a durable outbox (see
*The PostgreSQL outbox*): each declares the module to your application, and a module declared twice is
refused.

| | Configuration section | Vault |
| --- | --- | --- |
| Configurations | one for the single compartment; or one for the shared space, and one per compartment that has its own | one per space |
| Where it lives | `appsettings.json`, environment variables, user secrets, any configuration provider | a secret per space, in the vault of your choice |
| Editing from the application | no: the configuration is deployed with the application | yes, through `IMessagingConfigStore` |
| A change takes effect | at the next send, when the configuration provider reloads | at the next send once evicted from the cache (see *Keeping the cached configuration fresh*) |
| Dependencies | none beyond the core | `Stellaegis.Secrets`, `Stellaegis.Caching` |

`AddStellaegisEmail` registers the outbox worker as a hosted service, so the application must run as a
.NET host (`WebApplication`, `Host.CreateApplicationBuilder`, or a worker service). By default the
outbox is kept in memory: messages waiting for a retry are lost if the process stops. For a durable
outbox, add the PostgreSQL store (see *The PostgreSQL outbox*).

The host refuses to start when the assembly does not hold, and the error names what to change:

- without a source of configuration, the error names `AddStellaegisEmailConfiguration` and
  `AddStellaegisEmailVaultConfiguration`;
- without a transport, it names `AddStellaegisMailKitTransport`;
- in an application whose frame has no compartment, the validation of the frame by
  `Stellaegis.Primitives` refuses the messaging module, and so does an inheritance of the shared space
  turned on in a frame that declares none. An ASP.NET Core host runs that validation by itself; a host
  without ASP.NET Core registers it with `AddStellaegisValidation()`, or calls
  `services.ValidateStellaegisModules()` once its services are built;
- with an option that cannot work — an outbox batch of zero, a database without a passphrase for its
  active slot —, the validation of the options names the setting. The options of every package are
  validated this way, by `AddStellaegisOptions` of `Stellaegis.Primitives.Hosting`;
- with a PostgreSQL outbox whose database is not installed, or installed from another version of the
  package, the verification of the database names what is missing (see *The PostgreSQL outbox*).

Without these checks, a missing source or transport would only show at the first send, and the outbox
worker would fail at each of its cycles.

Then send:

```csharp
public sealed class InvitationMailer(IEmailSender email)
{
    public Task SendAsync(Compartment organization, string to, string html, CancellationToken ct) =>
        email.SendAsync(organization, new EmailMessage
        {
            To = [new EmailAddress(to)],
            Subject = "You are invited",
            HtmlBody = html,
        }, ct);
}
```

A `Compartment` converts to a `Space` implicitly. An application with a single compartment passes it,
`AppFrame.Owner`, to every send. `Space.Shared` is for mail that belongs to no compartment, in an
application with several.

### During development

Two choices are available while no relay exists:

- `AddStellaegisLoggingEmail()` registers a sender that writes each message to the console and to the
  log, and sends nothing. It needs no configuration.
- `AddStellaegisEmail` with no configuration for the space does the same: a space without a relay has
  its messages written to the log, never sent. This keeps the whole pipeline in place, so that adding
  a configuration later is enough to start sending.

Both write to the log, and nowhere else: your host decides where the log goes.

## Spaces

The library partitions its configuration and its outbox by **space**, a type of
`Stellaegis.Primitives`. A space is either a compartment — a tenant, an organization, any partition
your application names — or the shared space, for what belongs to no compartment.

A configuration section is read against the frame of your application: in an application with a single
compartment, it describes that compartment; in an application with several, it describes the shared
space, and each compartment may have a sub-section of its own (see *The configuration of a space*). In a
vault, the configuration of each space is one secret. A secret path of `Stellaegis.Secrets` has the shape
`{space}.{module}.{name}`, so that every secret of a compartment lies under one prefix:

| Space | Secret path |
| --- | --- |
| compartment `acme` | `acme.messaging.configuration` |
| shared space | `_shared.messaging.configuration` |

The shared space uses `_shared` because no compartment can be called that: the grammar of a
compartment allows lowercase letters, digits and hyphens only. A readable name such as `shared` or
`default` would be a valid compartment, which would then read and overwrite the shared configuration.

The underscore has a cost. The vault formats of `Stellaegis.Secrets` accept it — configuration,
PostgreSQL and Scaleway. A vault that accepts only letters, digits and hyphens in its secret names,
such as Azure Key Vault, needs a name format that translates it.

### Inheriting the shared configuration

By default, a compartment without a configuration of its own has no relay: its messages are written to
the log and not sent. Turn on the parameter `MessagingModule.InheritSharedSpace` to let such a
compartment use the configuration of the shared space instead:

```csharp
builder.Services.AddStellaegisFrame(ApplicationFrame.ManyCompartments().WithSharedSpace());
builder.Services.AddStellaegisModuleParameter<MessagingModule>(MessagingModule.InheritSharedSpace);
```

The inheritance is a capability that the module declares, and a choice that your application makes. It is
written in code, next to the frame, rather than in a configuration file: every host of the application
then makes the same choice, where a configuration could differ from one host to another. It supposes a
shared space, which the frame declares with `WithSharedSpace()`: turned on in an application without one,
the parameter fails the startup.

| | Inheritance off (default) | Inheritance on |
| --- | --- | --- |
| A compartment without configuration | sends nothing, logs its messages | sends through the shared relay, under the shared sender addresses |
| What it protects | No compartment reaches the shared relay unless you decide so | Every compartment can send from the first day |
| What it costs | Each compartment needs its configuration before it sends | Mail of a compartment leaves under an identity that is not its own |

A compartment that has its own configuration always uses it, whatever the parameter says. An application
with a single compartment needs no inheritance: its configuration section is that compartment's.

## The configuration of a space

In a configuration section, `Stellaegis:Messaging:Configuration` by default — the configuration of the
single compartment, or of the shared space in an application with several:

```json
{
  "Stellaegis": {
    "Messaging": {
      "Configuration": {
        "Transports": {
          "default": {
            "Host": "smtp.example.com",
            "Port": 587,
            "Security": "StartTls",
            "Auth": "Basic",
            "Credentials": { "Username": "relay-user" },
            "FromPolicy": { "Kind": "DomainAuthorized", "Domains": [ "example.com" ] },
            "DefaultFrom": { "Address": "noreply@example.com", "Name": "Example" },
            "EnvelopeFrom": "bounces@example.com",
            "OnFromMismatch": "Degrade"
          }
        },
        "FromAddresses": [
          { "Address": "billing@example.com", "Transport": "default", "ReplyTo": "accounts@example.com" }
        ]
      }
    }
  }
}
```

Each field can come from another configuration source. The password, for instance, from an environment
variable:

```sh
Stellaegis__Messaging__Configuration__Transports__default__Credentials__Password=…
```

In an application with several compartments, a compartment whose relay is deployed with the application has
its own sub-section, of the same shape, under `Stellaegis:Messaging:Compartments`:

```json
{
  "Stellaegis": {
    "Messaging": {
      "Configuration": { "Transports": { "default": { "Host": "smtp.example.com" } } },
      "Compartments": {
        "globex": { "Transports": { "default": { "Host": "smtp.globex.com" } } }
      }
    }
  }
}
```

In a vault, the secret holds the same structure as a JSON document. Property names are read without
regard to case.

```json
{
  "transports": {
    "default": {
      "host": "smtp.example.com",
      "credentials": { "username": "relay-user", "password": "…" },
      "fromPolicy": { "kind": "DomainAuthorized", "domains": [ "example.com" ] },
      "defaultFrom": { "address": "noreply@example.com" }
    }
  }
}
```

- `Transports` names one or more relays. The one named `default` is used unless the message, or the
  sender address, names another.
- `Security` is `None`, `StartTls` (port 587, the default) or `SslOnConnect` (port 465).
- `Auth` is `Basic` (user name and password, or API key), `ClientCert` (a client certificate, given as a
  base64 PFX in `Credentials.CertificatePfxBase64`, with `CertificatePassword`), or `None` (a relay
  that trusts the source address).
- `EnvelopeFrom` is the envelope sender — the address in the SMTP `MAIL FROM` command, which SPF
  checks. It must belong to the relay's domain.
- `FromAddresses` is optional. An entry binds a sender address to a transport, or gives it a default
  `Reply-To`.
- `DefaultFrom`, next to `Transports`, is the sender of a message that sets none, when its transport
  has no `DefaultFrom` of its own.

## The sender-address policy

A relay accepts some `From` addresses and refuses others. The `FromPolicy` of a transport tells the
library which, so that a message the relay would refuse is corrected before it leaves, or refused with
an explicit error.

| `Kind` | Accepted `From` | When the `From` is not accepted |
| --- | --- | --- |
| `DomainAuthorized` | any address in one of `Domains` | `OnFromMismatch: Degrade` (default): `From` becomes the transport's `DefaultFrom`, the display name is kept, and the original address moves to `Reply-To`. `Reject`: the send throws `EmailRejectedException`. In both cases a `Sender` outside the domains is removed |
| `MailboxBound` | the relay's own mailbox only, `DefaultFrom` | `From` becomes the mailbox, and an original address that differs moves to `Reply-To`. `Sender` is always removed |
| `IpUnrestricted`, or no policy | any address | — |

A message without `From` takes the transport's `DefaultFrom`, then the configuration's. Degrading keeps
the send going; rejecting tells the caller at once. Each transport makes that choice for itself.

What `SendAsync` refuses is a `UserFacingException` of `Stellaegis.Primitives`, whose `Error.Code` says
what to correct; an ASP.NET Core host answers it with the matching status. The codes are the constants of
`MessagingErrors`:

| Code | Type | When |
| --- | --- | --- |
| `Email.SenderRejected` | validation | the `From` is not authorized and the transport rejects (`EmailRejectedException`) |
| `Email.TransportUnknown` | validation | the message names a transport the configuration does not hold |
| `Email.FromRequired` | validation | the message has no `From`, and nothing provides a default one |
| `Email.TransportMisconfigured` | failure | a transport that degrades, or a mailbox-bound one, has no `DefaultFrom` to use |

## Keeping the cached configuration fresh

A configuration section is read at each send, so it needs no cache: a configuration provider that
reloads — a JSON file with `reloadOnChange`, for instance — takes effect on the next message.

The vault is read through a cache: one hour by default, in a bounded cache of `Stellaegis.Caching`. An
edit must therefore evict the cached copy to take effect before the hour ends.

- An edit made through `IMessagingConfigStore` (next section) evicts the cached copy in the process
  that makes it.
- An edit made elsewhere — by another process, or directly in the vault — is evicted by your
  application. The library subscribes to no event. When your application learns that a secret changed,
  from its message bus for instance, it calls `IMessagingConfigCache.Invalidate` with the path of that
  secret:

  ```csharp
  bus.Subscribe<SecretChanged>(evt => configCache.Invalidate(SecretPath.Parse(evt.Path)));
  ```

  A path outside the messaging configuration evicts nothing, so every secret change can be passed on
  without filtering.

Without either, the time-to-live is the only freshness mechanism. Options, in the `Stellaegis:Messaging`
section:

| Option | Default | Role |
| --- | --- | --- |
| `ConfigCacheTtl` | `01:00:00` | vault only: how long a configuration stays cached when no eviction comes |
| `ConfigCacheSizeLimit` | derived from available memory | vault only: the number of configurations held |

## Editing a configuration

`IMessagingConfigStore` reads and writes the configuration of a space for an editing surface, such as an
administration page. `Stellaegis.Messaging.Secrets` implements it on the vault: register it with
`AddStellaegisMessagingConfigStore(builder.Configuration)`; it needs an `IMutableSecretsProvider`.

- `GetAsync` returns the configuration with its secrets — passwords, certificates — replaced by
  `MessagingConfigMasking.MaskedSecret`. An editing page never receives a live secret.
- `SaveAsync` keeps the stored value of every secret field that comes back masked or empty. A page that
  only changes the relay's port therefore keeps the password it never saw.
- The store reads the space it is given and nothing else: it never falls back to the shared space.

A configuration section has no store: it is deployed with the application, and edited where it lives.

## The outbox

The outbox guarantees that a message is delivered at least once, or kept as a dead letter.

- The worker polls for due messages and **claims** them for a lease. A claimed message is invisible to
  other claims until the lease ends; if the process stops in the middle of a send, the message becomes
  due again when its lease expires.
- A failed attempt is retried after `RetryBackoffBase × 2^attempts`, capped at `RetryBackoffCap`.
- The transport makes two short retries of its own first, after 1 and 2 seconds, so that a passing
  network failure does not wait for the outbox.
- After `MaxAttempts`, the message becomes a dead letter, kept for `DeadLetterTtl`.

Several hosts of one application — the instances behind a load balancer — each run a worker over the same
durable outbox. A claim leases what it returns, so two workers never deliver the same message at once;
with the in-memory outbox, each host holds its own messages.

At least once means that a message can be sent twice: when the relay accepted it but the process stopped
before recording the success. An **idempotency key** on the message protects against the other source of
duplicates, a caller that retries:

```csharp
await email.SendAsync(space, message with { IdempotencyKey = $"invitation:{invitationId}" }, ct);
```

While the outbox holds a message with the same key in the same space, whatever its status, a second
`SendAsync` adds nothing. The protection lasts as long as the first message stays in the outbox. With
the default `SentRetention` of zero, a sent message is deleted at once, so the key protects the time the
message waits for delivery. Set `SentRetention` to keep sent messages, and the key, for longer.

Options, in the `Stellaegis:Messaging:Outbox` section:

| Option | Default | Role |
| --- | --- | --- |
| `PollingInterval` | `00:00:05` | how often the worker looks for due messages |
| `ClaimBatchSize` | `20` | messages claimed per poll |
| `LeaseDuration` | `00:05:00` | how long a claimed message stays invisible |
| `MaxAttempts` | `10` | attempts before a message becomes a dead letter |
| `RetryBackoffBase` | `00:01:00` | base of the retry delay |
| `RetryBackoffCap` | `01:00:00` | longest retry delay |
| `SentRetention` | `00:00:00` | how long a sent message is kept; zero deletes it at once |
| `DeadLetterTtl` | `30.00:00:00` | how long a dead letter is kept |
| `PurgeInterval` | `01:00:00` | how often expired messages are purged |

### When a compartment leaves

`IMessageOutboxPurge.PurgeCompartmentAsync(compartment, actor)` deletes every message of a compartment,
pending ones included, so that nothing of a compartment that leaves your application is sent afterwards.
It is a separate interface from `IMessageOutbox`, which every sender takes, so that only the code in charge
of administration holds it. The purge is replayable, logs the actor when it starts and ends, and returns
the number of messages deleted.

```csharp
await outboxPurge.PurgeCompartmentAsync(compartment, Actor.Job("purge"), ct);
```

## The PostgreSQL outbox

```csharp
builder.Services.AddStellaegisEmailPostgreSql(builder.Configuration);
builder.Services.AddStellaegisDatabaseVerification();   // Stellaegis.Primitives.Data
```

`AddStellaegisEmailPostgreSql` registers the engine with the durable store, in place of
`AddStellaegisEmail`: call one of the two.

The schema, `msg`, is **installed** by `DatabaseLifecycle.InstallAsync` of `Stellaegis.Primitives.Data`,
from an installer or a provisioning step, under a role that may create the schema and the pgcrypto
extension. The package declares its database as `MessagingDatabase.Value`, with its scripts embedded:

```csharp
// The installer, under a role that owns the schema.
await DatabaseLifecycle.InstallAsync([MessagingDatabase.Value], AppFrame.Value, OpenAsAdminAsync, "myapp_app");
```

The installation applies the scripts once, records each in `msg._schema_migrations`, and grants the
application role, `myapp_app` here, its data rights. It requires PostgreSQL 15 or later. Your application
connects under that role, and **verifies** the database at every startup: `AddStellaegisDatabaseVerification`
registers the verification as a hosted service — an ASP.NET Core host, whose server starts first, calls
`app.Services.VerifyStellaegisDatabasesAsync()` before running instead. A database not installed, or
installed from another version of the package, fails the start and names what to run. To delete every
message during development: `delete from msg.outbox`.

```json
{
  "Stellaegis": {
    "Messaging": {
      "Database": {
        "ConnectionString": "Host=localhost;Database=messaging;Username=myapp_app;...",
        "Keys": { "1": "set from an environment variable, never committed" }
      }
    }
  }
}
```

To set the connection string from code instead, follow the registration with
`services.PostConfigure<MessagingDatabaseOptions>(o => ...)`.

### Encryption at rest

A message holds personal data: its recipients and its body. The store encrypts it with pgcrypto's
`pgp_sym_encrypt`. The passphrase comes from the configuration of the process, never from a table or
from the vault, so the outbox does not depend on another store to read its own messages.

One passphrase protects the messages of every compartment. A key per compartment would let you make a
deleted compartment's messages unreadable by destroying its key. The outbox is a short-lived buffer,
though — a sent message is deleted, a dead letter is purged — so there is little such a key would
protect, and deleting a compartment's rows achieves the same.

For the same reason, the outbox table has no row-level security rule. A single worker sends the messages
of every compartment, and a rule restricting each connection to one compartment would hide them from it.
The store reaches the table on its own connections, outside the units of work of
`Stellaegis.Primitives.Data`: the outbox is a buffer of the sending engine, not data of the application.

The passphrases are the `KeySlots` of `Stellaegis.Primitives`, two slots with one active, as every secret
of the family that rotates. To change the passphrase without re-encrypting messages in flight, fill the
other slot and make it active:

```json
"Database": { "ActiveKeySlot": 2, "Keys": { "1": "old passphrase", "2": "new passphrase" } }
```

New messages use the active slot, and each row records the slot that encrypted it. Remove the old
passphrase once no row uses its slot: `select count(*) from msg.outbox where key_slot = 1`. The host
refuses to start when the active slot has no passphrase.

## Migrating from the Stellaegis framework

The library was part of the Stellaegis framework, where it knew tenants. What changes:

- **A space replaces the tenant.** `IEmailSender` has a single `SendAsync(Space, EmailMessage)`; the
  overload that read an ambient tenant context is gone. `IMessageOutbox`, `OutboxEntry`,
  `IEmailConfigResolver` and `IMessagingConfigStore` take or return a `Space`.
- **The vault is a package of its own.** `AddStellaegisEmail` no longer reads the vault: add
  `Stellaegis.Messaging.Secrets` and call `AddStellaegisEmailVaultConfiguration`, which replaces
  `AddStellaegisEmailConfigResolver`. `AddStellaegisMessagingConfigStore` lives there too, and takes the
  configuration, since the store evicts what it changes.
- **Vault paths.** `messaging.{tenant}` becomes `{compartment}.messaging.configuration`, for a slug that
  is a valid compartment. `messaging.default` becomes `_shared.messaging.configuration`. Move those
  secrets.
- **No automatic fallback to the shared configuration.** Turn on the parameter
  `MessagingModule.InheritSharedSpace` to keep the former behaviour.
- **No subscription.** `UseStellaegisMessagingSecretsBackplane` is gone: subscribe to your secret-change
  event and call `IMessagingConfigCache.Invalidate`, in the namespace `Stellaegis.Messaging.Secrets`.
  Paths are `SecretPath` values of `Stellaegis.Secrets`.
- **Renamed options.** `SecretCacheTtl` and `SecretCacheSizeLimit` become `ConfigCacheTtl` and
  `ConfigCacheSizeLimit`. A configuration file that keeps the old keys raises no error: they are
  ignored, and the defaults apply.
- **The transport** is `Stellaegis.Messaging.MailKit`, registered by `AddStellaegisMailKitTransport`.
  `AddStellaegisEmailAutoSelect` is gone: register `AddStellaegisEmail` or `AddStellaegisLoggingEmail`
  explicitly.
- **No templates.** `ITemplateEmailSender`, `ISystemTemplateProvider` and `AddStellaegisSystemTemplates`
  are gone, with the default templates. Render the body with your template engine —
  `Stellaegis.Rendering`, for instance — and send an `EmailMessage`.
- **The PostgreSQL outbox** reads `ConnectionString` from its section; `ConnectionStringName` is gone. Its
  schema changes: `tenant_slug` becomes `compartment`, null for the shared space, and `purge_at` is null
  while a message waits. Let the old outbox drain, then create the new schema with the script.

## Building and testing

```sh
dotnet test --solution Stellaegis.Messaging.slnx
```

The PostgreSQL tests need a server. By default they connect to `127.0.0.1` as `postgres`, and create a
database named `stellaegis_messaging_tests` if it does not exist. Set
`STELLAEGIS_MESSAGING_TEST_POSTGRES` to another connection string to change that. The tests install the
outbox as an application does, with `DatabaseLifecycle.InstallAsync` under that connection, then run
under an ordinary role, `stellaegis_messaging_app`, created by the fixture. When no server answers, these
tests are skipped and the others still run. The publishing workflow runs them against a PostgreSQL
container and fails when one is skipped.

The SMTP transport is tested up to the message it builds; the tests open no SMTP connection.

## License

MIT — see [LICENSE](https://github.com/Stellaegis/Stellaegis.Messaging/blob/main/LICENSE). The packages
depend on third-party components listed in
[THIRD-PARTY-NOTICES.md](https://github.com/Stellaegis/Stellaegis.Messaging/blob/main/THIRD-PARTY-NOTICES.md).
