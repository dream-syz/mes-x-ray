namespace MesXray.Domain.Graph;

/// <summary>How an edge should be interpreted when walking the graph.</summary>
public enum FlowDirection
{
    /// <summary>from depends on to: "to" is upstream (a source), "from" is downstream (a consumer).</summary>
    DependsOn,

    /// <summary>Data flows from "from" to "to": "from" is upstream, "to" is downstream.</summary>
    FlowsTo,

    /// <summary>Containment / typing; not data flow. Impact propagates from member to container.</summary>
    Structural,
}

/// <summary>
/// Single source of truth for the direction semantics of every <see cref="RelationType"/>.
/// Trace Source walks upstream; Impact walks downstream. Both are defined purely in terms of these semantics,
/// so adding a relation type only requires a decision here.
/// </summary>
public static class RelationSemantics
{
    public static FlowDirection DirectionOf(RelationType relation) => relation switch
    {
        RelationType.Contains => FlowDirection.Structural,
        RelationType.OfType => FlowDirection.Structural,

        RelationType.HandledBy => FlowDirection.DependsOn,
        RelationType.Calls => FlowDirection.DependsOn,
        RelationType.ExecutesSp => FlowDirection.DependsOn,
        RelationType.EnrichedBy => FlowDirection.DependsOn,
        RelationType.BranchesOn => FlowDirection.DependsOn,
        RelationType.UsesParameter => FlowDirection.DependsOn,
        RelationType.Reads => FlowDirection.DependsOn,
        RelationType.CallsFunction => FlowDirection.DependsOn,
        RelationType.ComputedBy => FlowDirection.DependsOn,
        RelationType.AliasOf => FlowDirection.DependsOn,
        RelationType.DerivedFrom => FlowDirection.DependsOn,
        RelationType.ControlledBy => FlowDirection.DependsOn,
        RelationType.DependsOn => FlowDirection.DependsOn,

        RelationType.Returns => FlowDirection.FlowsTo,
        RelationType.MapsTo => FlowDirection.FlowsTo,
        RelationType.SerializesAs => FlowDirection.FlowsTo,
        RelationType.Produces => FlowDirection.FlowsTo,

        _ => throw new ArgumentOutOfRangeException(nameof(relation), relation, "Relation has no declared flow direction."),
    };

    /// <summary>
    /// Relations that carry column/field-level lineage. Field traces follow only these so that a field trace
    /// does not explode into every table an SP happens to read.
    /// </summary>
    public static bool IsLineage(RelationType relation) => relation is
        RelationType.SerializesAs or
        RelationType.MapsTo or
        RelationType.Produces or
        RelationType.AliasOf or
        RelationType.DerivedFrom or
        RelationType.ControlledBy or
        RelationType.ComputedBy or
        RelationType.EnrichedBy;

    /// <summary>Relations describing the static execution path from an endpoint down to SQL objects.</summary>
    public static bool IsExecutionPath(RelationType relation) => relation is
        RelationType.Calls or
        RelationType.HandledBy or
        RelationType.ExecutesSp or
        RelationType.CallsFunction or
        RelationType.EnrichedBy;

    /// <summary>
    /// Given an edge and the node we are standing on, returns the node on the upstream side (toward sources),
    /// or null when the edge does not lead upstream from <paramref name="currentNodeId"/>.
    /// </summary>
    public static string? UpstreamNeighbor(Edge edge, string currentNodeId)
    {
        var direction = DirectionOf(edge.RelationType);
        if (direction == FlowDirection.DependsOn && edge.FromNodeId == currentNodeId)
        {
            return edge.ToNodeId;
        }

        if (direction == FlowDirection.FlowsTo && edge.ToNodeId == currentNodeId)
        {
            return edge.FromNodeId;
        }

        return null;
    }

    /// <summary>
    /// Given an edge and the node we are standing on, returns the node on the downstream side (toward consumers),
    /// or null when the edge does not lead downstream from <paramref name="currentNodeId"/>.
    /// </summary>
    public static string? DownstreamNeighbor(Edge edge, string currentNodeId)
    {
        var direction = DirectionOf(edge.RelationType);
        if (direction == FlowDirection.DependsOn && edge.ToNodeId == currentNodeId)
        {
            return edge.FromNodeId;
        }

        if (direction == FlowDirection.FlowsTo && edge.FromNodeId == currentNodeId)
        {
            return edge.ToNodeId;
        }

        return null;
    }
}
