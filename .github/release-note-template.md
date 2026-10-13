# {{TAG}}

Perezosoft Platform packages `{{VERSION}}` (ADR-034), released from `{{REF}}` at `{{SHA}}`.

## Schema change
{{SCHEMA_CHANGE}}

<!-- yes: the app bumps the packages, runs `dotnet ef migrations add`, and PlatformSchemaTests must pass; name every
     ApplyPlatformStep(name) the migration has to call. no: nothing to migrate. -->

## Register change
{{REGISTER_CHANGE}}

<!-- The register files (hosts, AppComposition.cs, project files, CI caller, ...) that change, and the recipe:
     `dotnet platform port` / `pwsh tools/port-platform.ps1 -To {{TAG}} -Apply`. none: nothing to port. -->

## Breaking
{{BREAKING}}

<!-- yes: what an app must change, in order. no: a package bump is enough. -->

## Packages
{{PACKAGES}}
