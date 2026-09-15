import { readFileSync } from "node:fs";

// Yarn Classic's --level changes the report, not its exit code.
try {
  const records = readFileSync(process.argv[2], "utf8").trim().split(/\r?\n/).map(JSON.parse);
  const summary = records.find(record => record.type === "auditSummary")?.data?.vulnerabilities;
  const severities = ["info", "low", "moderate", "high", "critical"];
  if (!summary || records.some(record => record.type === "error") ||
      severities.some(key => !Number.isInteger(summary[key]) || summary[key] < 0)) {
    throw new Error("The audit did not return a complete vulnerability summary.");
  }
  const expectedStatus = severities.reduce((status, key, index) =>
    status | (summary[key] > 0 ? 1 << index : 0), 0);
  if (Number(process.argv[3]) !== expectedStatus) {
    throw new Error("The audit failed independently of its vulnerability report.");
  }
  console.log(severities.map(key => `${key}: ${summary[key]}`).join(", "));
  process.exitCode = summary.moderate + summary.high + summary.critical > 0 ? 1 : 0;
} catch (error) {
  console.error(`Dependency audit failed: ${error.message}`);
  process.exitCode = 1;
}
