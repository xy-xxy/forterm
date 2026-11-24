import { test } from "node:test";
import assert from "node:assert/strict";
import {
  DEFAULT_LAYOUT,
  normalizeLayout,
  ratioBounds,
  clamp,
} from "../src/renderer/layout";
test("layout restores older/malformed preferences safely and excludes unrelated data", () => {
  assert.deepEqual(normalizeLayout(null), DEFAULT_LAYOUT);
  assert.deepEqual(
    normalizeLayout({
      sidebar: NaN,
      panel: Infinity,
      editor: "250",
      horizontal: "false",
    }),
    DEFAULT_LAYOUT,
  );
  const layout = normalizeLayout({
    sidebar: -100,
    panel: 99999,
    editor: 300,
    filesRatio: -1,
    horizontalRatio: 0.7,
    horizontal: true,
    password: "not-layout",
  });
  assert.equal(layout.sidebar, 180);
  assert.equal(layout.panel, 700);
  assert.equal(layout.filesRatio, 0.1);
  assert.equal(layout.horizontalRatio, 0.7);
  assert.equal(layout.horizontal, true);
  assert.ok(!JSON.stringify(layout).includes("password"));
});
test("shrinking split containers keeps both panes positive and preferred ratio recoverable", () => {
  const preference = 0.75;
  for (const length of [1, 40, 160, 400, 1200]) {
    const bounds = ratioBounds(length, 180, 180);
    const effective = clamp(preference, bounds.min, bounds.max);
    assert.ok(effective > 0 && effective < 1);
    assert.ok(bounds.max >= bounds.min);
  }
  const normal = ratioBounds(1200, 180, 180);
  assert.equal(clamp(preference, normal.min, normal.max), preference);
});

test("three-column widths reserve usable terminal space at 150% and restore independent preferences", async () => {
  const { workspacePaneWidths } = await import("../src/renderer/layout");
  for (const width of [0, 200, 413, 439, 700, 1200])
    for (const files of [false, true])
      for (const monitor of [false, true]) {
        const p = workspacePaneWidths(width, files, monitor, 300, 260);
        assert.ok(p.fileWidth >= 0 && p.monitorWidth >= 0);
        assert.ok(p.fileWidth + p.monitorWidth <= p.budget + 0.001);
        if (width >= 413 && files && monitor) {
          assert.ok(p.fileWidth >= 96 && p.monitorWidth >= 96);
          assert.ok(width - p.fileWidth - p.monitorWidth - 14 >= 160);
        }
      }
  const restored = workspacePaneWidths(1200, true, true, 300, 260);
  assert.equal(restored.fileWidth, 300);
  assert.equal(restored.monitorWidth, 260);
  const migrated = normalizeLayout({
    panel: 380,
    sidebar: 240,
    editorDock: "left",
  });
  assert.equal(migrated.filesWidth, 380);
  assert.equal(migrated.monitorVisible, false);
  assert.equal(migrated.navigationVisible, true);
  assert.equal(migrated.editorDock, "left");
  assert.equal(
    normalizeLayout({ filesWidth: 64, monitorWidth: 64 }).filesWidth,
    64,
  );
});
