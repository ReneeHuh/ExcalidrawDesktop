import { test } from "node:test";
import assert from "node:assert/strict";
import { randomUUID } from "node:crypto";
import { writeFileSync, unlinkSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { spawnSync } from "node:child_process";

const summary = counts => ({ type: "auditSummary", data: { vulnerabilities:
  { info: 0, low: 0, moderate: 0, high: 0, critical: 0, ...counts } } });

for (const [name, records, yarnStatus, expectedStatus] of [
  ["accepts a clean audit", [summary({})], 0, 0],
  ["allows low advisories", [summary({ low: 1 })], 2, 0],
  ["rejects moderate advisories", [summary({ moderate: 1 })], 4, 1],
  ["rejects missing summaries", [{ type: "error", data: "network failure" }], 1, 1],
  ["rejects operational failures even with a clean summary", [summary({})], 1, 1],
]) {
  test(name, () => {
    const path = join(tmpdir(), `excalidraw-audit-${randomUUID()}.json`);
    try {
      writeFileSync(path, records.map(record => JSON.stringify(record)).join("\n"));
      const result = spawnSync(process.execPath,
        [fileURLToPath(new URL("./Check-YarnAudit.mjs", import.meta.url)), path, String(yarnStatus)],
        { encoding: "utf8" });
      assert.equal(result.status, expectedStatus, result.stderr);
    } finally { unlinkSync(path); }
  });
}
