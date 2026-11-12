# Synthetic ADVERT vectors

These are synthetic, not captured radio traffic. The observer key (`ab` repeated
32 times) deliberately differs from the advertised full key.

Pinned format: MeshCore `81dbae592ffad40b4212bc0b37e82b38da08f4bc`,
`Mesh.cpp`, `createAdvert` and the ADVERT receive branch. Header/path `1100`
means ADVERT, wire version bits `00`, flood, empty path. Timestamp bytes are
`01020304`, preserved as little-endian signed content, not a claimed hearing time.

Signatures were generated independently of NSec using installed OpenSSL 3.6.5:

1. Use deterministic **public test seed** bytes `00..1f`; never use it for real keys.
2. DER private key is hex `302e020100300506032b657004220420` followed by the seed.
3. `openssl pkey -in seed.der -inform DER -pubout -outform DER` supplies the last
   32 bytes as public key: `03a107bff3ce10be1d70dd18e74bc09967e4d6309ba50d5f1ddc8664125531b8`.
4. Write `public_key || 01020304 || app_data` to `message.bin`.
5. `openssl pkeyutl -sign -inkey seed.der -keyform DER -rawin -in message.bin`
   produces the detached signature.
6. Packet is `1100 || public_key || 01020304 || signature || app_data`.

All scratch for generation belongs under the ignored repository `.tmp/`.

| Appdata | Signature |
| --- | --- |
| empty | `5339ad035b8c52c2698c00d1f227e63660544c0ad74d6d4590262bc22a7403eca9f8b58912d1c03d8a9b7badf82ec55e972b2034edaf6836ecf158590be18b06` |
| `8266697874757265` | `4a38058c25a4c0703e335480f0c4bc54174293bbc072c877ea7969d418e9d16e0a8000dfa46ebb21ccbf6376625ed18cf2ca3502745e81498abd316b51d7eb0e` |
| `8266697874757265` followed by 24 zero bytes | `05b7576579e6d0108004fea284ced4fc7694ad8568cb135b568ce82087f4adbb37228eff393e0be32678d34bbb808dce009124a63e82305a2103bf9044d81701` |

These establish runtime and format verification, not executed firmware or live-feed compatibility.
