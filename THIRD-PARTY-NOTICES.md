# Third-party notices

Stellaegis.Messaging is licensed under the MIT License (see [LICENSE](LICENSE)).

Its packages contain no third-party code. They reference the packages below, which NuGet installs
alongside them, each under its own license.

| Component | Used by | License (SPDX) | Project |
| --- | --- | --- | --- |
| MailKit, and MimeKit under it | `Stellaegis.Messaging.MailKit` | MIT | <https://github.com/jstedfast/MailKit> |
| System.Security.Cryptography.Pkcs | `Stellaegis.Messaging.MailKit` | MIT | <https://github.com/dotnet/runtime> |
| Npgsql | `Stellaegis.Messaging.PostgreSQL` | PostgreSQL | <https://github.com/npgsql/npgsql> |
| Dapper | `Stellaegis.Messaging.PostgreSQL` | Apache-2.0 | <https://github.com/DapperLib/Dapper> |
| Stellaegis.Primitives, Stellaegis.Primitives.Data | all packages | MIT | <https://github.com/Stellaegis/Stellaegis.Primitives> |
| Stellaegis.Secrets | `Stellaegis.Messaging` | MIT | <https://github.com/Stellaegis/Stellaegis.Secrets> |
| Stellaegis.Caching | `Stellaegis.Messaging` | MIT | <https://github.com/Stellaegis/Stellaegis.Caching> |
| Microsoft.Extensions.\* | all packages | MIT | <https://github.com/dotnet> |

System.Security.Cryptography.Pkcs is referenced directly by `Stellaegis.Messaging.MailKit`, at a version
that fixes known vulnerabilities of the one MimeKit asks for.

Test-only dependencies (xUnit) are not part of any package and are not listed here. The PostgreSQL
server that the tests and the publishing workflow use is not part of any package either.
