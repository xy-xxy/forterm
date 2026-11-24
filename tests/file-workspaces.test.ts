import { test } from "node:test";
import assert from "node:assert/strict";
import { FileWorkspaces } from "../src/main/file-workspaces";
test("legacy active source migrates to explicit tabs, inactive shared references survive switching, and removal stays scoped", () => {
  const active = new Map([["owner-a", "files-a"]]),
    groups = new FileWorkspaces(active);
  assert.deepEqual(groups.group("owner-a").sourceIds, ["owner-a", "files-a"]);
  groups.attach("owner-a", "files-b");
  groups.attach("owner-b", "files-a");
  groups.select("owner-a", "files-a");
  assert.deepEqual(groups.group("owner-a").sourceIds, [
    "owner-a",
    "files-a",
    "files-b",
  ]);
  assert.deepEqual(groups.referencing("files-b"), ["owner-a"]);
  groups.detach("owner-a", "files-a");
  assert.equal(groups.group("owner-a").activeId, "files-b");
  assert.deepEqual(groups.referencing("files-a"), ["owner-b"]);
  groups.removeWorkspace("owner-a");
  assert.deepEqual(groups.referencing("files-a"), ["owner-b"]);
  groups.removeSource("files-a");
  assert.equal(groups.group("owner-b").activeId, "owner-b");
  assert.deepEqual(groups.group("owner-b").sourceIds, ["owner-b"]);
  groups.replace("owner-b", "files-c");
  assert.deepEqual(groups.group("owner-b").sourceIds, ["owner-b", "files-c"]);
  assert.throws(() => groups.select("owner-b", "unassociated"), /先明确关联/);
});
