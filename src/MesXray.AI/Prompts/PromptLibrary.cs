using System.Reflection;
using System.Text;
using System.Text.Json;
using MesXray.AI.Evidence;

namespace MesXray.AI.Prompts;

/// <summary>Loads prompt templates from embedded resources and renders the evidence bundle into them.</summary>
public static class PromptLibrary
{
    private static readonly Assembly Assembly = typeof(PromptLibrary).Assembly;

    public static string System() => Load("explain.system.md");

    public static string User(EvidenceBundle bundle, bool investigate)
    {
        var template = Load("explain.user.md");
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["task"] = investigate
                ? "Investigate the question below using only the evidence bundle. Produce steps, facts, hypotheses and unknowns."
                : "Explain where the focus value comes from, how it is computed and under which conditions, using only the evidence bundle.",
            ["focus"] = EvidenceBundleBuilder.DescribeNode(bundle.Focus) + $" (id {bundle.Focus.Id})",
            ["question"] = bundle.Question ?? "(none - explain the focus value)",
            ["trace"] = bundle.TraceId is null ? "(no live trace)" : $"traceId={bundle.TraceId} scope={bundle.Scope ?? "-"} environment={bundle.Environment ?? "-"}",
            ["executionPath"] = bundle.ExecutionPath.Count == 0 ? "(unknown)" : string.Join(" -> ", bundle.ExecutionPath.Select(n => $"{n.Name} [{n.Id}]")),
            ["hops"] = RenderHops(bundle),
            ["evidence"] = string.Join("\n", bundle.Items.Where(i => bundle.AllowedEvidenceIds.Contains(i.Id)).Select(i => $"- {i.Id} ({i.Kind}): {i.Text}")),
            ["unknowns"] = bundle.Unknowns.Count == 0 ? "(none)" : string.Join("\n", bundle.Unknowns.Select(u => "- " + u)),
        };

        var sb = new StringBuilder(template);
        foreach (var (key, value) in values)
        {
            sb.Replace("{{" + key + "}}", value);
        }

        return sb.ToString();
    }

    /// <summary>JSON schema of the structured output (mirrors <see cref="Contracts.Explanation"/> minus audit).</summary>
    public static JsonElement ExplanationSchema()
    {
        const string schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["summary", "steps", "knownFacts", "hypotheses", "unknowns", "nextSteps", "confidence", "verdict"],
          "properties": {
            "summary": { "type": "string" },
            "steps": { "type": "array", "items": { "type": "string" } },
            "knownFacts": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["text", "evidenceIds"],
                "properties": {
                  "text": { "type": "string" },
                  "evidenceIds": { "type": "array", "items": { "type": "string" } }
                }
              }
            },
            "hypotheses": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["text", "status"],
                "properties": {
                  "text": { "type": "string" },
                  "status": { "type": "string", "enum": ["unverified", "supported", "refuted"] },
                  "suggestedCheck": { "type": "string" },
                  "evidenceIds": { "type": "array", "items": { "type": "string" } }
                }
              }
            },
            "unknowns": { "type": "array", "items": { "type": "string" } },
            "nextSteps": { "type": "array", "items": { "type": "string" } },
            "confidence": { "type": "number", "minimum": 0, "maximum": 1 },
            "verdict": { "type": "string", "enum": ["known", "needMoreEvidence", "unknown"] }
          }
        }
        """;
        using var document = JsonDocument.Parse(schema);
        return document.RootElement.Clone();
    }

    private static string RenderHops(EvidenceBundle bundle)
    {
        var sb = new StringBuilder();
        foreach (var hop in bundle.Hops)
        {
            sb.Append(' ', hop.Depth * 2)
              .Append("- ").Append(hop.Name).Append(" [").Append(hop.NodeId).Append("] ")
              .Append(hop.Type).Append(' ').Append(hop.Status);
            if (hop.ViaRelation is not null)
            {
                sb.Append(" via ").Append(hop.ViaRelation).Append(" (").Append(hop.ViaEdgeId).Append(')');
            }

            if (hop.Expression is not null)
            {
                sb.Append(" expr: ").Append(hop.Expression);
            }

            if (hop.Condition is not null)
            {
                sb.Append(" when ").Append(hop.Condition);
            }

            if (hop.RuntimeValueTexts.Count > 0)
            {
                sb.Append(" runtime: ").Append(string.Join("; ", hop.RuntimeValueTexts)).Append(" (").Append(string.Join(", ", hop.RuntimeEvidenceIds)).Append(')');
            }

            if (hop.IsRepeat)
            {
                sb.Append(" (repeat)");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string Load(string name)
    {
        var resource = Assembly.GetManifestResourceNames().FirstOrDefault(r => r.EndsWith(name, StringComparison.Ordinal))
                       ?? throw new InvalidOperationException($"Prompt resource '{name}' not found.");
        using var stream = Assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
