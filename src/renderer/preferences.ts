export interface Preferences {
  bastionCollapsed: boolean;
  light: boolean;
  fontSize: number;
  panel: boolean;
  collapsed: string[];
}
const key = "forterm.workspace.v1";
export function readPreferences(): Preferences {
  const defaults: Preferences = {
    bastionCollapsed: false,
    light: false,
    fontSize: 14,
    panel: true,
    collapsed: [],
  };
  try {
    const raw = JSON.parse(localStorage.getItem(key) || "{}");
    return {
      bastionCollapsed:
        typeof raw.bastionCollapsed === "boolean"
          ? raw.bastionCollapsed
          : false,
      light: typeof raw.light === "boolean" ? raw.light : defaults.light,
      fontSize:
        Number.isInteger(raw.fontSize) &&
        raw.fontSize >= 10 &&
        raw.fontSize <= 24
          ? raw.fontSize
          : defaults.fontSize,
      panel: typeof raw.panel === "boolean" ? raw.panel : defaults.panel,
      collapsed: Array.isArray(raw.collapsed)
        ? raw.collapsed
            .filter((s: unknown) => typeof s === "string")
            .slice(0, 1000)
        : [],
    };
  } catch {
    return defaults;
  }
}
export function writePreferences(value: Preferences) {
  try {
    localStorage.setItem(key, JSON.stringify(value));
  } catch {
    /* Workspace remains usable if storage is unavailable. */
  }
}
