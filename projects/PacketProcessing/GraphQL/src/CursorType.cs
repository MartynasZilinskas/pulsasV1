using System.Text.Json;
using HotChocolate.Features;
using HotChocolate.Language;
using HotChocolate.Text.Json;

namespace Pulsas.PacketProcessing.GraphQL;

public class CursorType() : BrowserStringType("Cursor");

public abstract class BrowserStringType(string name) : ScalarType<string, StringValueNode>(name)
{
    protected override string OnCoerceInputLiteral(StringValueNode valueSyntax) =>
        valueSyntax.Value;

    protected override string OnCoerceInputValue(JsonElement jsonValue, IFeatureProvider context) =>
        jsonValue.ValueKind == JsonValueKind.String
            ? jsonValue.GetString()!
            : throw new FormatException("Expected a string.");

    protected override void OnCoerceOutputValue(string runtimeValue, ResultElement resultValue) =>
        resultValue.SetStringValue(runtimeValue);

    protected override StringValueNode OnValueToLiteral(string runtimeValue) => new(runtimeValue);
}
