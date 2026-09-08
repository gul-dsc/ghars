// Fixture manifest for Ghars runtime verification runs.
//
// A test run records every row it creates, by id, at the moment it creates it. Cleanup then
// deletes exactly those ids (see Remove-GharsFixtures.ps1). Nothing is ever identified later by
// subject text, program name, notification wording, or "rows created in the last few hours" —
// those match legitimate data too, and on one pass a '/bookings/details/2[0-9]' pattern matched
// booking 20 alongside the fixtures and deleted two real notification rows.
//
// Usage:
//
//   import { createRun } from '<repo>/tools/testing/fixture-manifest.mjs';
//   const run = createRun('dual-booking');
//   const id = run.recordBookingFromRedirect(res.headers['location'], 'existing-program E2E');
//   ...
//   run.record('activity', draftActivityId, 'draft offering for the approval gate');
//
// The manifest is written through to disk on every record, so a run that crashes half way still
// leaves behind an accurate list of what it made.

import { readFileSync, writeFileSync, mkdirSync, existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const RUNS = join(HERE, 'runs');

// Fixture kind -> the table its id lives in. Cleanup understands exactly these shapes.
const KIND_TABLE = {
  booking: 'BookingRequests',
  activity: 'Activities',
  organization: 'Organizations',
  contactMessage: 'ContactMessages',
  notification: 'Notifications',
  agendaEntry: 'AgendaEntries',
};

/**
 * Start a run. Requires a baseline captured by New-GharsBaseline.ps1 BEFORE the run started —
 * without its high-water marks, cleanup has no way to prove an id was created by this run, which
 * is the whole point of the exercise.
 */
export function createRun(name, { baselinePath = join(RUNS, 'baseline.json') } = {}) {
  if (!existsSync(baselinePath)) {
    throw new Error(
      `No baseline at ${baselinePath}. Run tools/testing/New-GharsBaseline.ps1 before the test run.`);
  }
  const baseline = JSON.parse(readFileSync(baselinePath, 'utf8'));
  if (baseline.schema !== 'ghars-test-baseline/1') {
    throw new Error(`Unrecognised baseline schema '${baseline.schema}'.`);
  }

  const runId = `${new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19)}-${name}`;
  const path = join(RUNS, `${runId}.json`);
  if (!existsSync(RUNS)) mkdirSync(RUNS, { recursive: true });

  const manifest = {
    schema: 'ghars-test-manifest/1',
    runId,
    name,
    startedAtUtc: new Date().toISOString(),
    baseline,
    fixtures: [],
    modified: [],
  };

  const flush = () => writeFileSync(path, JSON.stringify(manifest, null, 2), 'utf8');
  flush();

  const api = {
    runId,
    path,
    baseline,

    /** The baseline high-water mark for a table, or -1 if it was not tracked. */
    watermark(table) {
      return Object.prototype.hasOwnProperty.call(baseline.watermarks, table)
        ? baseline.watermarks[table] : -1;
    },

    /**
     * Record a row this run created.
     *
     * Rejects an id at or below the baseline watermark immediately. That case means the id was
     * captured wrongly — SCOPE_IDENTITY() read from the wrong batch, a redirect parsed loosely,
     * a hard-coded leftover — and it is far better to fail here than to hand cleanup an id that
     * points at somebody else's row.
     */
    record(kind, id, note = '') {
      const table = KIND_TABLE[kind];
      if (!table) {
        throw new Error(`Unknown fixture kind '${kind}'. Known: ${Object.keys(KIND_TABLE).join(', ')}.`);
      }
      const numeric = Number(id);
      if (!Number.isInteger(numeric) || numeric <= 0) {
        throw new Error(`Fixture id for ${kind} must be a positive integer; got ${JSON.stringify(id)}.`);
      }
      const mark = api.watermark(table);
      if (mark >= 0 && numeric <= mark) {
        throw new Error(
          `Refusing to record ${table}:${numeric} — the baseline high-water mark is ${mark}, so that ` +
          `row existed before this run. Check how the id was captured.`);
      }
      if (!manifest.fixtures.some(f => f.kind === kind && f.id === numeric)) {
        manifest.fixtures.push({ kind, id: numeric, note, recordedAtUtc: new Date().toISOString() });
        flush();
      }
      return numeric;
    },

    /**
     * Record a booking from the redirect a successful POST returns.
     *
     * This is the id-capture pattern to prefer everywhere: take the id the application itself
     * just handed back, at the moment it hands it back. No later lookup can be as certain.
     */
    recordBookingFromRedirect(location, note = '') {
      const match = /\/bookings\/details\/(\d+)(?:$|[?#])/.exec(location ?? '');
      if (!match) throw new Error(`No booking id in redirect location: ${JSON.stringify(location)}`);
      return api.record('booking', Number(match[1]), note);
    },

    /**
     * Record that the run is about to change an existing row, with its exact current value so it
     * can be put back byte for byte. Capture the value BEFORE changing it — a value reconstructed
     * afterwards from what you assume it was is not a restoration.
     */
    recordModification(table, id, column, originalValue) {
      manifest.modified.push({
        table, id: Number(id), column, original: originalValue ?? null,
        recordedAtUtc: new Date().toISOString(),
      });
      flush();
      return originalValue;
    },

    /** Fixture ids of one kind, for assertions inside the run. */
    idsOf(kind) {
      return manifest.fixtures.filter(f => f.kind === kind).map(f => f.id);
    },

    /** Close the run. The file is already current; this just stamps the finish time. */
    finish() {
      manifest.finishedAtUtc = new Date().toISOString();
      flush();
      console.log(`   run   manifest: ${path} (${manifest.fixtures.length} fixtures, ` +
                  `${manifest.modified.length} modified rows)`);
      return path;
    },
  };

  return api;
}

/**
 * Notifications carry no foreign key to what they describe — only a LinkUrl string. Cleanup can
 * therefore match them exactly when the link contains the row id (/bookings/details/28), but NOT
 * when it does not (/partner/programs). For those, record the notification id explicitly:
 *
 *   SELECT Id FROM dbo.Notifications WHERE Id > <run.watermark('Notifications')> ORDER BY Id;
 *
 * run immediately after the action that raised it, then run.record('notification', id).
 */
export const NOTIFICATION_CAPTURE_NOTE = 'See fixture-manifest.mjs for link-less notifications.';
