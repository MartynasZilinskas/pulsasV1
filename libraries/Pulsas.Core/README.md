# Pulsas.Core

Shared domain primitives without dependencies on transport codecs, EF Core, or
spatial persistence libraries.

`GeoLocation.FromMicrodegrees` creates an immutable geographic location
with inclusive latitude bounds of ±90° and longitude bounds of ±180°. Integer
microdegrees preserve the supplied precision. The Funcky `Option<GeoLocation>`
returns `None` for invalid coordinates and `Some` for valid coordinates.

`(0,0)` is a valid geographic location. Protocol-specific omission and reset
semantics belong to the caller. Conversion to NetTopologySuite/PostGIS belongs
to Persistence, with longitude as X and latitude as Y.
