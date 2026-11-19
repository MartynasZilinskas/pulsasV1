using HotChocolate.Language;
using HotChocolate.Validation;

namespace Pulsas.Gateway.ApiService;

internal class RootReadLimitRule : IDocumentValidatorRule
{
    // Match legacy admission: each distinct root response key can trigger a read.
    private const int MaximumRootFields = 4;

    public ushort Priority => ushort.MaxValue;
    public bool IsCacheable => true;

    public void Validate(DocumentValidatorContext context, DocumentNode document)
    {
        var fragments = new Dictionary<string, FragmentDefinitionNode>(StringComparer.Ordinal);
        foreach (var fragment in document.Definitions.OfType<FragmentDefinitionNode>())
        {
            if (!fragments.TryAdd(fragment.Name.Value, fragment))
            {
                return; // The standard validation rules reject duplicate definitions.
            }
        }

        foreach (var operation in document.Definitions.OfType<OperationDefinitionNode>())
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<SelectionSetNode>();
            pending.Push(operation.SelectionSet);
            while (pending.TryPop(out var set))
            {
                foreach (var selection in set.Selections)
                {
                    switch (selection)
                    {
                        case FieldNode field:
                            names.Add(field.Alias?.Value ?? field.Name.Value);
                            if (names.Count > MaximumRootFields)
                            {
                                context.ReportError(
                                    ErrorBuilder
                                        .New()
                                        .SetMessage("The operation exceeds the root field limit.")
                                        .SetCode("RESOURCE_LIMIT")
                                        .Build()
                                );
                                return;
                            }
                            break;
                        case InlineFragmentNode inline:
                            pending.Push(inline.SelectionSet);
                            break;
                        case FragmentSpreadNode spread
                            when visited.Add(spread.Name.Value)
                                && fragments.TryGetValue(spread.Name.Value, out var fragment):
                            pending.Push(fragment.SelectionSet);
                            break;
                    }
                }
            }
        }
    }
}
