import { Handle, Position, type NodeProps } from "@xyflow/react";
import { LAYER_COLORS, TYPE_LABELS, formatValue, shortName } from "../../lib/presentation";
import type { XRayFlowNode } from "./layout";

export function XRayNode({ data, selected }: NodeProps<XRayFlowNode>) {
  const { node, runtimeValues, highlighted, focus, onPath } = data;
  const palette = LAYER_COLORS[node.layer] ?? LAYER_COLORS.service;
  const gap = node.status !== "known";
  const classes = ["xray-node", `status-${node.status}`, highlighted ? "highlighted" : "", focus ? "focus" : "", onPath ? "on-path" : "", selected ? "selected" : ""].join(" ");
  const style = {
    background: gap ? "#fff" : palette.fill,
    borderColor: focus ? "#111" : palette.border,
    color: palette.text,
  };

  return (
    <div className={classes} style={style} title={node.qualifiedName ?? node.id}>
      <Handle type="target" position={Position.Left} className="handle" />
      <div className="xray-node-type">
        <span>{TYPE_LABELS[node.type] ?? node.type}</span>
        <span className="layer-chip" style={{ background: palette.border }}>
          {palette.label}
        </span>
      </div>
      <div className="xray-node-name">{shortName(node)}</div>
      {gap && <div className="xray-node-gap">{node.status === "pending" ? "Pending" : "Unknown"} · Need More Evidence</div>}
      {runtimeValues.length > 0 && (
        <div className="xray-node-values">
          {runtimeValues.slice(0, 2).map((v) => (
            <span key={v.evidenceId} className="value-chip" title={`${v.label} (${v.evidenceId}, ${v.evidenceType})`}>
              {v.scope ? `${v.scope}: ` : ""}
              {formatValue(v.value)}
            </span>
          ))}
          {runtimeValues.length > 2 && <span className="value-chip more">+{runtimeValues.length - 2}</span>}
        </div>
      )}
      <Handle type="source" position={Position.Right} className="handle" />
    </div>
  );
}
