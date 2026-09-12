import { Handle, Position, type NodeProps } from "@xyflow/react";
import { Warning } from "@phosphor-icons/react";
import type { CSSProperties } from "react";
import { useI18n } from "../../lib/I18nContext";
import { formatValue, shortName } from "../../lib/presentation";
import type { XRayFlowNode } from "./layout";

export function XRayNode({ data, selected }: NodeProps<XRayFlowNode>) {
  const { node, runtimeValues, highlighted, focus, onPath, dimmed, index, direction } = data;
  const { t, type, layer, statusShort } = useI18n();
  const gap = node.status !== "known";
  // Edges run in data-flow direction: upward when ranked bottom-to-top, rightward when ranked left-to-right.
  const [targetSide, sourceSide] = direction === "BT" ? [Position.Bottom, Position.Top] : [Position.Left, Position.Right];
  const classes = [
    "xray-node",
    `status-${node.status}`,
    highlighted ? "highlighted" : "",
    focus ? "focus" : "",
    onPath ? "on-path" : "",
    dimmed && !selected ? "dimmed" : "",
    selected ? "selected" : "",
  ]
    .filter(Boolean)
    .join(" ");

  return (
    <div className={classes} style={{ "--i": index } as CSSProperties} title={node.qualifiedName ?? node.id}>
      <Handle type="target" position={targetSide} className="handle" />
      <div className="xray-node-type">
        <span>{type(node.type)}</span>
        <span className="layer-tag">{layer(node.layer)}</span>
      </div>
      <div className="xray-node-name">{shortName(node)}</div>
      {gap && (
        <div className="xray-node-gap">
          <Warning size={12} weight="bold" />
          {t("node.gap", { status: statusShort(node.status) })}
        </div>
      )}
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
      <Handle type="source" position={sourceSide} className="handle" />
    </div>
  );
}
