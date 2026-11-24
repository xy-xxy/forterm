import { test } from "node:test";
import assert from "node:assert/strict";
import { textFormat, convertLineEndings } from "../src/renderer/text-format";
import { terminalThemes } from "../src/renderer/terminal-themes";
test("format classification preserves BOM, tails, no final newline and detects mixed breaks", () => {
  const raw = "\ufeffa:\t1  \r\n  b: 2";
  const result = textFormat(raw);
  assert.equal(result.bom, true);
  assert.equal(result.mixed, false);
  assert.equal(result.eol, "\r\n");
  assert.equal(result.normalized, "a:\t1  \n  b: 2");
  assert.equal(raw, "\ufeffa:\t1  \r\n  b: 2");
  assert.equal(textFormat("a\r\nb\nc").mixed, true);
  assert.equal(textFormat("a\rb").mixed, true);
});
test("only explicit conversion changes line breaks and retains all other characters", () => {
  assert.equal(
    convertLineEndings("\ufeffa\r\nb\nc  ", "\n"),
    "\ufeffa\nb\nc  ",
  );
  assert.equal(convertLineEndings("a\nb\r\nc  ", "\r\n"), "a\r\nb\r\nc  ");
});
const luminance = (s: string) => {
  const rgb = [1, 3, 5]
    .map((i) => parseInt(s.slice(i, i + 2), 16) / 255)
    .map((x) => (x <= 0.04045 ? x / 12.92 : ((x + 0.055) / 1.055) ** 2.4));
  return rgb[0] * 0.2126 + rgb[1] * 0.7152 + rgb[2] * 0.0722;
};
test("light terminal palette covers normal/bright ANSI colors and readable green/yellow/blue", () => {
  const theme = terminalThemes.light;
  for (const key of [
    "background",
    "foreground",
    "cursor",
    "cursorAccent",
    "selectionBackground",
    "black",
    "red",
    "green",
    "yellow",
    "blue",
    "magenta",
    "cyan",
    "white",
    "brightBlack",
    "brightRed",
    "brightGreen",
    "brightYellow",
    "brightBlue",
    "brightMagenta",
    "brightCyan",
    "brightWhite",
  ] as const)
    assert.ok(theme[key]);
  const bg = luminance(theme.background!);
  for (const key of [
    "foreground",
    "green",
    "yellow",
    "blue",
    "brightGreen",
    "brightYellow",
    "brightBlue",
  ] as const) {
    const fg = luminance(theme[key]!);
    assert.ok(
      (Math.max(bg, fg) + 0.05) / (Math.min(bg, fg) + 0.05) >= 4.5,
      key,
    );
  }
});
