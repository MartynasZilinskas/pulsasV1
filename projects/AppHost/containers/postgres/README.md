# Local PostgreSQL image

Aspire run mode builds this image from `postgis/postgis:18-3.6` and adds PGMQ
**1.13.1**. The upstream source archive is version-pinned and SHA-256 checked;
PGMQ is SQL/PLpgSQL, so no compiler or extra service is needed. Extension upgrade
scripts are included. A cold build requires access to the base-image registry
and GitHub. The base image tag follows the existing local PostgreSQL setup.

The image supplies extension files; it does **not** itself enable PGMQ or initialize
the application schema. Aspire's separate `database-setup` resource now applies EF
migrations (including PostGIS and node geography triggers) to `pulsas_dev`. It does
not enable PGMQ. The existing `pulsas-v1-postgres-data` volume is retained. No image
startup SQL or automatic extension upgrades are added. Do not remove volumes to
switch images.

Deployment uses the provider's image, not this Containerfile. Provisioning must
supply PostGIS and a compatible PGMQ version. Npgmq's grouped-head API requires
PGMQ >= **1.11.1**; the currently verified pair is Npgmq **1.13.0** / PGMQ
**1.13.1**. Provider compatibility and extension provisioning are not yet tested.

For the disposable transaction/ordering spike, see
[Worker tests](../../../../tests/Pulsas.Worker.Tests/README.md). This is not yet
a production queue consumer or a throughput benchmark.
