# Traffic generator tests

Normal `dotnet test` runs payload/configuration and local stalled-connection tests.
Broker tests are skipped unless explicitly enabled: they publish synthetic data to the local broker and
controls the generator on ports 9001 and 5300. Do not run it against a stack in use.

Start an isolated local verification run with these settings:

```sh
TrafficGenerator__Rounds=10 TrafficGenerator__IntervalMilliseconds=100 ./aspire.sh
```

Then, in another terminal:

```sh
PULSAS_LOCAL_MQTT_TESTS=1 mise exec -- dotnet test tests/Pulsas.TrafficGenerator.Tests --no-restore
```

The test uses 3 observers, VNO, and QoS 1 (the defaults), checks real subscriber
delivery and ADVERT signatures, and exercises concurrent start/stop and restart.
Stop Aspire with Ctrl+C afterward; preserve existing volumes. MQTT delivery is
not evidence of Worker ingestion or database persistence.
