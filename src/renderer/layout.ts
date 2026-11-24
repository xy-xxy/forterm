export interface WorkspaceLayout {
  navigationVisible: boolean;
  filesVisible: boolean;
  monitorVisible: boolean;
  filesWidth: number;
  monitorWidth: number;
  editorDock: "right" | "left" | "bottom";
  editorWidth: number;
  auxHeight: number;
  sidebar: number;
  panel: number;
  editor: number;
  filesRatio: number;
  verticalRatio: number;
  horizontalRatio: number;
  horizontal: boolean;
}
export const DEFAULT_LAYOUT: Readonly<WorkspaceLayout> = {
  navigationVisible: true,
  filesVisible: true,
  monitorVisible: false,
  filesWidth: 280,
  monitorWidth: 260,
  editorDock: "right",
  editorWidth: 380,
  auxHeight: 180,
  sidebar: 254,
  panel: 320,
  editor: 250,
  filesRatio: 0.55,
  verticalRatio: 0.5,
  horizontalRatio: 0.5,
  horizontal: false,
};
export const LAYOUT_KEY = "forterm.layout.v1";
export function clamp(value: number, min: number, max: number) {
  return Math.min(Math.max(value, min), Math.max(min, max));
}
export function ratioBounds(
  length: number,
  firstMin: number,
  secondMin: number,
) {
  const available = Math.max(1, length - 7);
  const scale =
    available < firstMin + secondMin
      ? (0.8 * available) / (firstMin + secondMin)
      : 1;
  return {
    min: (firstMin * scale) / available,
    max: 1 - (secondMin * scale) / available,
    pixelsPerUnit: available,
  };
}
export function normalizeLayout(raw: unknown): WorkspaceLayout {
  const input =
    raw && typeof raw === "object" ? (raw as Record<string, unknown>) : {};
  const number = (key: keyof WorkspaceLayout, min: number, max: number) =>
    typeof input[key] === "number" && Number.isFinite(input[key])
      ? clamp(input[key] as number, min, max)
      : (DEFAULT_LAYOUT[key] as number);
  return {
    navigationVisible:
      typeof input.navigationVisible === "boolean"
        ? input.navigationVisible
        : true,
    filesVisible:
      typeof input.filesVisible === "boolean" ? input.filesVisible : true,
    monitorVisible:
      typeof input.monitorVisible === "boolean" ? input.monitorVisible : false,
    filesWidth:
      typeof input.filesWidth === "number" && Number.isFinite(input.filesWidth)
        ? clamp(input.filesWidth, 32, 700)
        : typeof input.panel === "number" && Number.isFinite(input.panel)
          ? clamp(input.panel, 32, 700)
          : DEFAULT_LAYOUT.filesWidth,
    monitorWidth: number("monitorWidth", 32, 700),
    editorDock: ["right", "left", "bottom"].includes(input.editorDock as string)
      ? (input.editorDock as WorkspaceLayout["editorDock"])
      : "right",
    editorWidth: number("editorWidth", 160, 700),
    auxHeight: number("auxHeight", 88, 600),
    sidebar: number("sidebar", 180, 480),
    panel: number("panel", 220, 700),
    editor: number("editor", 100, 1000),
    filesRatio: number("filesRatio", 0.1, 0.9),
    verticalRatio: number("verticalRatio", 0.1, 0.9),
    horizontalRatio: number("horizontalRatio", 0.1, 0.9),
    horizontal:
      typeof input.horizontal === "boolean" ? input.horizontal : false,
  };
}
export function readLayout(): WorkspaceLayout {
  try {
    const raw = JSON.parse(localStorage.getItem(LAYOUT_KEY) || "{}");
    if (
      raw &&
      typeof raw === "object" &&
      typeof raw.filesVisible !== "boolean"
    ) {
      const previous = JSON.parse(
        localStorage.getItem("forterm.workspace.v1") || "{}",
      );
      if (previous.panel === false) raw.filesVisible = false;
    }
    return normalizeLayout(raw);
  } catch {
    return { ...DEFAULT_LAYOUT };
  }
}
export function writeLayout(layout: WorkspaceLayout) {
  try {
    localStorage.setItem(LAYOUT_KEY, JSON.stringify(layout));
  } catch {
    /* Layout still works without storage. */
  }
}

export function workspacePaneWidths(
  width: number,
  files: boolean,
  monitor: boolean,
  preferredFiles: number,
  preferredMonitor: number,
) {
  const available = Math.max(0, width),
    centerMinimum = Math.min(
      260,
      available * 0.5,
      files && monitor ? Math.max(120, available - 238) : Infinity,
    ),
    gaps = (Number(files) + Number(monitor)) * 7;
  const budget = Math.max(0, available - centerMinimum - gaps),
    minFiles = files
      ? Math.min(
          140,
          monitor ? Math.max(112, budget * 0.25) : budget,
          monitor ? budget * 0.5 : budget,
        )
      : 0,
    minMonitor = monitor
      ? Math.min(
          140,
          files ? Math.max(112, budget * 0.25) : budget,
          files ? budget * 0.5 : budget,
        )
      : 0;
  let fileWidth = files
      ? clamp(preferredFiles, minFiles, budget - minMonitor)
      : 0,
    monitorWidth = monitor
      ? clamp(preferredMonitor, minMonitor, budget - minFiles)
      : 0;
  if (fileWidth + monitorWidth > budget) {
    const extras = fileWidth - minFiles + monitorWidth - minMonitor,
      ratio =
        extras > 0 ? Math.max(0, budget - minFiles - minMonitor) / extras : 0;
    fileWidth = minFiles + (fileWidth - minFiles) * ratio;
    monitorWidth = minMonitor + (monitorWidth - minMonitor) * ratio;
  }
  return {
    fileWidth,
    monitorWidth,
    budget,
    centerMinimum,
    fileBounds: { min: minFiles, max: Math.max(minFiles, budget - minMonitor) },
    monitorBounds: {
      min: minMonitor,
      max: Math.max(minMonitor, budget - minFiles),
    },
  };
}
