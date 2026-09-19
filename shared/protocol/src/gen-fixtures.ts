/**
 * Writes the canonical fixtures from fixtures.ts to shared/protocol/fixtures/.
 * Run via `npm run fixtures` (or `make fixtures` at the repo root).
 * Regenerate whenever the protocol types change — the .NET contract tests
 * will then flag any C# mirror that has drifted.
 */

import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { FIXTURES } from './fixtures.js';

const outDir = join(dirname(fileURLToPath(import.meta.url)), '..', 'fixtures');
mkdirSync(outDir, { recursive: true });

for (const [name, value] of Object.entries(FIXTURES)) {
  writeFileSync(join(outDir, name), JSON.stringify(value, null, 2) + '\n');
}

console.log(`wrote ${Object.keys(FIXTURES).length} fixtures to ${outDir}`);
