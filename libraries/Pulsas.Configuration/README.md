# Pulsas.Configuration

Loads a startup snapshot from one JSON file or the immediate `*.json` files in a
directory. Each top-level section can belong to only one selected file. Invalid
content fails atomically, with redacted JSON diagnostics. Files are not watched;
restart to apply changes.

Worker reads `PULSAS_SECRETS_PATH`, then reapplies environment variables and
command-line arguments in that precedence order. Relative paths use the process
working directory; absolute paths are recommended. Keep secrets out of source
control and command-line arguments.

.NET merges individual keys, including array indices; a shorter overriding array
does **not** remove lower-layer entries in this generic helper. Worker applies its
own catalogue-replacement rules when constructing its typed startup snapshot; see
[Worker configuration](../../configuration/README.md). This helper is not a secret
vault or live-reload mechanism.
