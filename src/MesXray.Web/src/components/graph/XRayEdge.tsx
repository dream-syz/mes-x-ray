import { BaseEdge, getSmoothStepPath, type EdgeProps } from "@xyflow/react";
import type { XRayFlowEdge } from "./layout";

/**
 * The smoothstep edge of React Flow plus a replay pulse: a dot that travels the edge once when the replay reaches it
 * (see replay.ts). The pulse is an SVG motion animation along the very path the edge is drawn with, so it follows every
 * bend; `reverse` sends it against the drawn direction (a call going down the execution path).
 */
export function XRayEdge({
  id,
  sourceX,
  sourceY,
  targetX,
  targetY,
  sourcePosition,
  targetPosition,
  label,
  labelStyle,
  labelBgStyle,
  labelBgPadding,
  labelBgBorderRadius,
  style,
  markerEnd,
  interactionWidth,
  data,
}: EdgeProps<XRayFlowEdge>) {
  const [path, labelX, labelY] = getSmoothStepPath({ sourceX, sourceY, sourcePosition, targetX, targetY, targetPosition });
  const pulse = data?.pulse;
  return (
    <>
      <BaseEdge
        id={id}
        path={path}
        labelX={labelX}
        labelY={labelY}
        label={label}
        labelStyle={labelStyle}
        labelBgStyle={labelBgStyle}
        labelBgPadding={labelBgPadding}
        labelBgBorderRadius={labelBgBorderRadius}
        style={style}
        markerEnd={markerEnd}
        interactionWidth={interactionWidth}
      />
      {pulse && (
        <circle key={pulse.key} className="edge-pulse" r={6}>
          <animateMotion dur={`${pulse.ms}ms`} repeatCount="1" fill="freeze" calcMode="linear" keyPoints={pulse.reverse ? "1;0" : "0;1"} keyTimes="0;1" path={path} />
        </circle>
      )}
    </>
  );
}
