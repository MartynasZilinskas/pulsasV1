namespace Pulsas.Worker.Tests.Simulation;

public record ObserverConfiguration(
    string Broker,
    string Region,
    string CountryCode,
    string Model,
    string FirmwareVersion
);
