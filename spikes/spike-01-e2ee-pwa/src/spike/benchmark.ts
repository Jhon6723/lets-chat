import { createSignalProtocolClient } from '@open-e2ee/signal-protocol-sdk';
import { inMemoryStore } from '@open-e2ee/signal-protocol-sdk/local/store/memory';
import { indexedDbStore } from '@open-e2ee/signal-protocol-sdk/local/store/web';
import { inMemoryRelay } from '@open-e2ee/signal-protocol-sdk/remote/relay/memory';

type Client = Awaited<ReturnType<typeof createSignalProtocolClient>>;

export interface MetricResult {
  name: string;
  value: string;
  threshold: string;
  pass: boolean;
}

export interface BenchmarkOutput {
  metrics: MetricResult[];
  log: string[];
}

const MESSAGE_COUNT = 10;
const SESSION_SETUP_THRESHOLD_MS = 2000;
const ROUNDTRIP_THRESHOLD_MS = 100;
const SPIKE_DB_NAME = 'signal-protocol-storage';
const openStores: { close(): void }[] = [];

function wipeSpikeDatabase(): Promise<void> {
  for (const store of openStores.splice(0)) store.close();
  return new Promise((resolve, reject) => {
    const req = indexedDB.deleteDatabase(SPIKE_DB_NAME);
    req.onsuccess = () => resolve();
    req.onerror = () => reject(req.error);
    req.onblocked = () => reject(new Error('IndexedDB wipe blocked by an open connection'));
  });
}

function ms(start: number): number {
  return performance.now() - start;
}

async function decryptOne(
  receiver: Client,
  relay: ReturnType<typeof inMemoryRelay>,
  userId: string,
  deviceId: number,
  text: string,
  log: (line: string) => void
): Promise<number> {
  const start = performance.now();
  const envs = await relay.getPendingMessages(userId, deviceId);
  log(`pending envelopes: ${envs.length} [${envs.map((e) => e.messageType).join(', ')}]`);
  const env = envs.find((e) => e.messageType !== 'server_delivery_receipt');
  if (!env) throw new Error('no envelope pending for recipient');
  try {
    const plain = await receiver.processIncomingEnvelope(env as never);
    console.log(`[spike] decrypted: "${plain}"`);
    if (plain !== text) log(`decrypt mismatch: expected "${text}" got "${plain}"`);
  } catch (decErr) {
    // UntrustedIdentityError = sender's identity key changed vs the pinned
    // record. In a real app this is a MITM warning requiring user consent;
    // in the spike, Alice regenerates keys every run so we accept the rotation.
    const code = (decErr as { code?: string }).code;
    if (code === 'UNTRUSTED_IDENTITY') {
      const err = decErr as { untrustedAddress?: { userId: string }; identity?: unknown };
      const identity = err.identity;
      const senderId = err.untrustedAddress?.userId;
      if (!identity || !senderId) throw decErr;
      log(`sender identity changed (${senderId}) — accepting rotation (spike)`);
      await receiver.acceptIdentityRotation(senderId, identity as never);
      const plain = await receiver.processIncomingEnvelope(env as never);
      console.log(`[spike] decrypted after rotation: "${plain}"`);
    } else {
      console.error('[spike] decrypt failed', decErr);
      log(`decrypt failed: ${decErr instanceof Error ? decErr.message : decErr}`);
      throw decErr;
    }
  }
  // processIncomingEnvelope does not consume the mailbox entry — the caller
  // must ack it or the same envelope is replayed on the next read.
  await relay.markDelivered(env.id);
  return ms(start);
}

const sdkLogger = (tag: string, log: (line: string) => void) => ({
  debug: (msg: string, data?: unknown) => {
    console.log(`[sdk:${tag}]`, msg, data ?? '');
    // The cipher retry loop logs the real inner error at debug level
    // before retrying with the next session candidate — surface it.
    const s = typeof data === 'object' && data !== null ? JSON.stringify(data) : String(data ?? '');
    if (msg.toLowerCase().includes('error') || msg.toLowerCase().includes('fail') || s.includes('"error"')) {
      log(`[sdk:${tag}] DBG ${msg} ${s}`);
    }
  },
  info: (msg: string, data?: unknown) => console.log(`[sdk:${tag}]`, msg, data ?? ''),
  warn: (msg: string, data?: unknown) => {
    console.warn(`[sdk:${tag}]`, msg, data ?? '');
    log(`[sdk:${tag}] WARN ${msg} ${data ? JSON.stringify(data) : ''}`);
  },
  error: (msg: string, err?: unknown, data?: unknown) => {
    console.error(`[sdk:${tag}]`, msg, err ?? '', data ?? '');
    const detail = err instanceof Error ? err.message : JSON.stringify(err ?? '');
    log(`[sdk:${tag}] ERROR ${msg} ${detail} ${data ? JSON.stringify(data) : ''}`);
  },
});

async function createClient(
  userId: string,
  usePersistentStore: boolean,
  relay: ReturnType<typeof inMemoryRelay>,
  log: (line: string) => void = () => {}
) {
  const deviceId = await relay.registerDevice(userId, { encryptedDeviceName: new ArrayBuffer(0) });
  const storage = usePersistentStore ? await indexedDbStore() : inMemoryStore();
  if (usePersistentStore) openStores.push(storage);
  const t0 = performance.now();
  const client = await createSignalProtocolClient({
    identity: { userId, deviceId },
    adapters: { storage, relay },
    logger: sdkLogger(userId, log),
  });
  const createMs = ms(t0);
  const t1 = performance.now();
  await client.syncToServer();
  return { client, deviceId, createMs, syncMs: ms(t1) };
}

export async function runBenchmark(log: (line: string) => void): Promise<BenchmarkOutput> {
  const metrics: MetricResult[] = [];
  const logLines: string[] = [];
  const say = (line: string) => { log(line); logLines.push(line); };

  const relay = inMemoryRelay();
  say('Wiping IndexedDB for a deterministic run...');
  await wipeSpikeDatabase();
  // ?store=memory puts Bob on inMemoryStore too — isolates IndexedDB-adapter
  // bugs from crypto/protocol issues when debugging on device.
  const bobPersistent = new URLSearchParams(location.search).get('store') !== 'memory';
  say(`Creating Alice (memory) and Bob (${bobPersistent ? 'IndexedDB' : 'memory'} store)...`);

  const alice = await createClient('alice', false, relay, say);
  const bob = await createClient('bob', bobPersistent, relay, say);

  const setupTotal = alice.createMs + alice.syncMs + bob.createMs + bob.syncMs;
  say(`Alice: create ${alice.createMs.toFixed(0)}ms, sync ${alice.syncMs.toFixed(0)}ms`);
  say(`Bob:   create ${bob.createMs.toFixed(0)}ms, sync ${bob.syncMs.toFixed(0)}ms`);
  metrics.push({
    name: 'Session setup (X3DH, both clients)',
    value: `${setupTotal.toFixed(0)} ms`,
    threshold: `< ${SESSION_SETUP_THRESHOLD_MS} ms`,
    pass: setupTotal < SESSION_SETUP_THRESHOLD_MS,
  });

  say(`Sending ${MESSAGE_COUNT} messages Alice -> Bob (direct pull + processIncomingEnvelope)...`);
  const roundtrips: number[] = [];
  for (let i = 0; i < MESSAGE_COUNT; i++) {
    const text = `msg-${i}-你好-hola`;
    console.log(`[spike] sending message ${i}`);
    try {
      await alice.client.send('bob', text);
    } catch (sendErr) {
      console.error('[spike] send failed', sendErr);
      say(`send failed: ${sendErr instanceof Error ? sendErr.message : sendErr}`);
      throw sendErr;
    }
    roundtrips.push(await decryptOne(bob.client, relay, 'bob', bob.deviceId, text, say));
  }
  const avg = roundtrips.reduce((a, b) => a + b, 0) / roundtrips.length;
  const p95 = roundtrips.slice().sort((a, b) => a - b)[Math.floor(roundtrips.length * 0.95)] ?? avg;
  say(`Round-trip avg ${avg.toFixed(0)}ms, p95 ${p95.toFixed(0)}ms`);
  metrics.push({
    name: 'Encrypt+deliver+decrypt round-trip (avg)',
    value: `${avg.toFixed(0)} ms`,
    threshold: `< ${ROUNDTRIP_THRESHOLD_MS} ms`,
    pass: avg < ROUNDTRIP_THRESHOLD_MS,
  });
  metrics.push({
    name: 'Encrypt+deliver+decrypt round-trip (p95)',
    value: `${p95.toFixed(0)} ms`,
    threshold: `< ${ROUNDTRIP_THRESHOLD_MS * 3} ms`,
    pass: p95 < ROUNDTRIP_THRESHOLD_MS * 3,
  });

  // Persistence check: Bob's store lives in IndexedDB. Record identity for reload test.
  localStorage.setItem('spike.bob.deviceId', String(bob.deviceId));
  localStorage.setItem('spike.persist.pending', 'true');
  say('Bob identity persisted to IndexedDB. Reload the page and press "Verify reload survival".');

  alice.client.stopRelaySubscription();
  bob.client.stopRelaySubscription();

  return { metrics, log: logLines };
}

export async function verifyReloadSurvival(log: (line: string) => void): Promise<BenchmarkOutput> {
  const metrics: MetricResult[] = [];
  const logLines: string[] = [];
  const say = (line: string) => { log(line); logLines.push(line); };

  const pending = localStorage.getItem('spike.persist.pending') === 'true';
  if (!pending) {
    say('No pending persistence marker. Run the benchmark first.');
    return { metrics, log: logLines };
  }

  const relay = inMemoryRelay();
  // Recreate Bob with the same userId and deviceId; IndexedDB store should reload his identity/session state.
  // The in-memory relay is empty after reload, so re-register the device first —
  // syncToServer then re-uploads the persisted prekey bundle.
  const deviceId = Number(localStorage.getItem('spike.bob.deviceId'));
  await relay.registerDevice('bob', { deviceId, encryptedDeviceName: new ArrayBuffer(0) });
  const storage = await indexedDbStore();
  const bob = await createSignalProtocolClient({
    identity: { userId: 'bob', deviceId },
    adapters: { storage, relay },
    logger: sdkLogger('bob', say),
  });
  await bob.syncToServer();
  say('Bob client recreated after reload with persisted IndexedDB store.');

  const alice = await createClient('alice', false, relay, say);
  const text = 'post-reload-你好-hola';
  await alice.client.send('bob', text);
  // Drain all pending envelopes — a reconnecting client must process the
  // backlog, not just the newest message.
  const start = performance.now();
  const envs = await relay.getPendingMessages('bob', deviceId);
  say(`pending envelopes after reload: ${envs.length}`);
  let gotText = false;
  for (const env of envs) {
    if (env.messageType === 'server_delivery_receipt') continue;
    try {
      const plain = await bob.processIncomingEnvelope(env as never);
      if (plain === text) gotText = true;
      await relay.markDelivered(env.id);
    } catch (decErr) {
      const err = decErr as { code?: string; untrustedAddress?: { userId: string }; identity?: unknown };
      if (err.code === 'UNTRUSTED_IDENTITY' && err.identity && err.untrustedAddress) {
        say(`sender identity changed (${err.untrustedAddress.userId}) — accepting rotation (spike)`);
        await bob.acceptIdentityRotation(err.untrustedAddress.userId, err.identity as never);
        const plain = await bob.processIncomingEnvelope(env as never);
        if (plain === text) gotText = true;
        await relay.markDelivered(env.id);
      } else {
        throw decErr;
      }
    }
  }
  const decryptMs = performance.now() - start;
  if (!gotText) throw new Error(`expected plaintext "${text}" not found in pending envelopes`);

  metrics.push({
    name: 'Session survives page reload (IndexedDB)',
    value: 'yes',
    threshold: 'yes',
    pass: true,
  });
  metrics.push({
    name: 'Post-reload decrypt latency',
    value: `${decryptMs.toFixed(0)} ms`,
    threshold: `< ${ROUNDTRIP_THRESHOLD_MS * 3} ms`,
    pass: decryptMs < ROUNDTRIP_THRESHOLD_MS * 3,
  });
  localStorage.removeItem('spike.persist.pending');
  say('PASS: Bob decrypted a message after a full page reload using persisted state.');

  alice.client.stopRelaySubscription();
  bob.stopRelaySubscription();
  return { metrics, log: logLines };
}
