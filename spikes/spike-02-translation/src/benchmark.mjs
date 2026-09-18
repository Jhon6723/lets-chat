/**
 * Spike 02 — Translation provider benchmark.
 *
 * Sends a battery of real chat messages through DeepL and Gemini Flash,
 * measuring latency and printing outputs side by side for quality review.
 *
 * Usage: node --env-file=.env src/benchmark.mjs [provider]
 *   provider: "deepl" | "deepseek" | "both" (default)
 */

const DEEPL_KEY = process.env.DEEPL_API_KEY;
const DEEPSEEK_KEY = process.env.DEEPSEEK_API_KEY;
const DEEPSEEK_MODEL = process.env.DEEPSEEK_MODEL ?? 'deepseek-flash';
// Free-tier keys end in :fx and hit api-free.deepl.com
const DEEPL_BASE = DEEPL_KEY?.endsWith(':fx') ? 'https://api-free.deepl.com' : 'https://api.deepl.com';

// Backend-owned system prompt for LLM providers (ADR 0003/0004).
// The client never sees or edits this.
const SYSTEM_PROMPT = `You are a translation engine. Translate the user's message from {src} to {tgt}. Output ONLY the translated text. No explanations, no quotes, no commentary. Preserve emojis, line breaks, formatting, URLs, and @mentions exactly. Keep code-switched segments in their original language. Match the register and tone of the source.`;

// Battery: real informal chat messages, zh -> es.
// Each entry stresses a different failure mode.
const MESSAGES = [
  { id: 'plain', text: '你吃饭了吗？' },
  { id: 'colloquial', text: '这也太离谱了吧，他居然真的这么干了' },
  { id: 'emoji', text: '今天累死了😭 加班到现在才下班' },
  { id: 'code-switch', text: '我mañana要开会，你别迟到' },
  { id: 'slang', text: '我裂开了，老板又改需求了' },
  { id: 'mention-url', text: '@小明 快看这个 https://example.com 笑死我了哈哈哈' },
  { id: 'idiomatic', text: '这事儿你看着办吧，我不管了' },
  { id: 'tone', text: '你别阴阳怪气的，有话直说' },
  { id: 'multiline', text: '明天上午十点开会\n记得带上方案\n别又忘了' },
  { id: 'long', text: '其实我之前就想跟你说了，但是一直没找到合适的机会。我觉得我们这样做下去不是办法，要不我们找个时间好好聊聊？' },
];

// BENCH_IDS=plain,slang runs only a subset — useful against tight quotas
const BENCH_IDS = process.env.BENCH_IDS?.split(',');
const ACTIVE_MESSAGES = BENCH_IDS ? MESSAGES.filter((m) => BENCH_IDS.includes(m.id)) : MESSAGES;

const SRC = 'zh';
const TGT = 'es';

async function translateDeepL(text) {
  const res = await fetch(`${DEEPL_BASE}/v2/translate`, {
    method: 'POST',
    headers: {
      'Authorization': `DeepL-Auth-Key ${DEEPL_KEY}`,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({
      text: [text],
      source_lang: 'ZH',
      target_lang: 'ES',
    }),
  });
  if (!res.ok) throw new Error(`DeepL ${res.status}: ${await res.text()}`);
  const data = await res.json();
  return data.translations[0].text;
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function withRetry(fn, attempts = 3) {
  let lastErr;
  for (let i = 0; i < attempts; i++) {
    try {
      return await fn();
    } catch (err) {
      lastErr = err;
      // 503 UNAVAILABLE / 429 rate limit: transient — back off and retry
      if (!/ 503| 429/.test(err.message)) throw err;
      await sleep(1000 * 2 ** i);
    }
  }
  throw lastErr;
}

// DeepSeek is OpenAI-compatible — same request shape, different base URL.
async function translateDeepSeek(text) {
  return withRetry(async () => {
    const res = await fetch('https://api.deepseek.com/chat/completions', {
      method: 'POST',
      headers: {
        'Authorization': `Bearer ${DEEPSEEK_KEY}`,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        model: DEEPSEEK_MODEL,
        temperature: 0,
        // Translation needs no reasoning — disable thinking mode for latency
        thinking: { type: 'disabled' },
        messages: [
          { role: 'system', content: SYSTEM_PROMPT.replace('{src}', SRC).replace('{tgt}', TGT) },
          { role: 'user', content: text },
        ],
      }),
    });
    if (!res.ok) throw new Error(`DeepSeek ${res.status}: ${await res.text()}`);
    const data = await res.json();
    return data.choices?.[0]?.message?.content?.trim();
  });
}

function stats(latencies) {
  const sorted = [...latencies].sort((a, b) => a - b);
  const avg = latencies.reduce((a, b) => a + b, 0) / latencies.length;
  const p50 = sorted[Math.floor(sorted.length * 0.5)];
  const p95 = sorted[Math.floor(sorted.length * 0.95)] ?? sorted[sorted.length - 1];
  return { avg, p50, p95 };
}

async function benchProvider(name, fn) {
  console.log(`\n${'='.repeat(60)}\n  ${name}\n${'='.repeat(60)}`);
  const latencies = [];
  const outputs = [];
  for (const msg of ACTIVE_MESSAGES) {
    const t0 = performance.now();
    try {
      const out = await fn(msg.text);
      const ms = performance.now() - t0;
      latencies.push(ms);
      outputs.push({ id: msg.id, src: msg.text, out });
      console.log(`  [${msg.id}] ${ms.toFixed(0)}ms`);
    } catch (err) {
      outputs.push({ id: msg.id, src: msg.text, out: `ERROR: ${err.message}` });
      console.log(`  [${msg.id}] FAILED: ${err.message}`);
    }
  }
  if (latencies.length) {
    const { avg, p50, p95 } = stats(latencies);
    console.log(`  --- ${latencies.length}/${MESSAGES.length} ok | avg ${avg.toFixed(0)}ms | p50 ${p50.toFixed(0)}ms | p95 ${p95.toFixed(0)}ms`);
  }
  return outputs;
}

const which = process.argv[2] ?? 'both';
const results = {};

if ((which === 'both' || which === 'deepl') && DEEPL_KEY) {
  results.deepl = await benchProvider('DeepL', translateDeepL);
}
if ((which === 'both' || which === 'deepseek') && DEEPSEEK_KEY) {
  results.deepseek = await benchProvider(`DeepSeek (${DEEPSEEK_MODEL})`, translateDeepSeek);
}

// Persist outputs so partial runs (quota exhaustion) still leave usable data
const { writeFileSync, readFileSync, existsSync } = await import('node:fs');
const RESULTS_FILE = new URL('../results.json', import.meta.url).pathname;
const prior = existsSync(RESULTS_FILE) ? JSON.parse(readFileSync(RESULTS_FILE, 'utf8')) : {};
const merged = { ...prior };
for (const [provider, outputs] of Object.entries(results)) {
  merged[provider] = merged[provider] ?? {};
  for (const o of outputs) merged[provider][o.id] = o.out;
}
writeFileSync(RESULTS_FILE, JSON.stringify(merged, null, 2));

// Side-by-side for quality review — includes results from prior partial runs
if (merged.deepl && merged.gemini) {
  console.log(`\n${'='.repeat(60)}\n  SIDE BY SIDE (zh -> es)\n${'='.repeat(60)}`);
  for (const m of MESSAGES) {
    const d = merged.deepl?.[m.id];
    const s = merged.deepseek?.[m.id];
    if (!d && !s) continue;
    console.log(`\n  [${m.id}] ${m.text}`);
    console.log(`    DeepL:    ${d ?? '(no result)'}`);
    console.log(`    DeepSeek: ${s ?? '(no result)'}`);
  }
}
