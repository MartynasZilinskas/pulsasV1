# Dummy Worker tests

Normal solution tests validate configuration and exercise generator reports through
the real packet handler, including recovery after invalid input. The opt-in broker test exercises
the production consumer, both subscriptions, ADVERT interpretation, and shutdown.
It publishes only synthetic, non-retained data in region `TST`.

Against an isolated local WebSocket broker:

```sh
PULSAS_WORKER_MQTT_ENDPOINT=ws://127.0.0.1:9001/mqtt \
  mise exec -- dotnet test tests/Pulsas.Worker.Tests --no-restore
```

Do not point this at a shared or external broker. This proves transport receipt
and in-memory ADVERT interpretation, not database persistence. For the generator path, run
Aspire, wait for Worker health, start traffic, and inspect Worker's interpretation logs.
