using System.Text.Json;
using HotChocolate.Features;
using HotChocolate.Language;
using HotChocolate.Text.Json;
using NodaTime;
using NodaTime.Text;

namespace Pulsas.PacketProcessing.GraphQL;

public class InstantType() : ScalarType<Instant, StringValueNode>("Instant")
{
    protected override Instant OnCoerceInputLiteral(StringValueNode valueSyntax) =>
        InstantPattern.ExtendedIso.Parse(valueSyntax.Value).GetValueOrThrow();

    protected override Instant OnCoerceInputValue(
        JsonElement jsonValue,
        IFeatureProvider context
    ) =>
        jsonValue.ValueKind == JsonValueKind.String
            ? InstantPattern.ExtendedIso.Parse(jsonValue.GetString()!).GetValueOrThrow()
            : throw new FormatException("Instant must be an ISO-8601 string.");

    protected override void OnCoerceOutputValue(Instant runtimeValue, ResultElement resultValue) =>
        resultValue.SetStringValue(InstantPattern.ExtendedIso.Format(runtimeValue));

    protected override StringValueNode OnValueToLiteral(Instant runtimeValue) =>
        new(InstantPattern.ExtendedIso.Format(runtimeValue));
}
