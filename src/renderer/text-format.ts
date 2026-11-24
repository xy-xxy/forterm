export function textFormat(raw: string) {
  const bom = raw.startsWith("\ufeff");
  const body = bom ? raw.slice(1) : raw;
  const endings = body.match(/\r\n|\r|\n/g) ?? [];
  const kinds = new Set(endings);
  return {
    bom,
    body,
    mixed: kinds.size > 1 || kinds.has("\r"),
    eol: endings[0] === "\r\n" ? "\r\n" : "\n",
    normalized: body.replace(/\r\n|\r/g, "\n"),
    endings,
  };
}
export function convertLineEndings(raw: string, eol: "\n" | "\r\n") {
  return raw.replace(/\r\n|\r|\n/g, eol);
}
