/**
 * Canonical wire-format samples. These objects are typed against the real
 * protocol types — if the spec changes, this file fails to compile before
 * stale fixtures can ever be generated. The .NET contract tests validate
 * the C# mirror types against the JSON emitted from these objects.
 */

import type {
  EncryptedEnvelope,
  PreKeyBundle,
  PreKeyBundleResponse,
  RelayClientMessage,
  RelayServerMessage,
} from './index.js';

export const envelope: EncryptedEnvelope = {
  version: 1,
  id: 'env-001',
  senderAddress: 'alice.1',
  recipientAddress: 'bob.1',
  type: 'message',
  ciphertext: 'a8f3b2c9d4e5f60718293a4b5c6d7e8f',
  header: 'f7e6d5c4b3a2918070605040302010',
  createdAt: 1758147600000,
};

export const prekeyBundle: PreKeyBundle = {
  address: 'bob.1',
  identityKey: 'BQaWxvdWxkLWJlLWEtcmVhbC1rZXktaGVyZQ==',
  signedPreKey: {
    id: 7,
    publicKey: 'BHNpZ25lZC1wcmVrZXktcHVia2V5',
    signature: 'c2lnbmVkLXByZWtleS1zaWduYXR1cmU=',
  },
  oneTimePreKey: {
    id: 42,
    publicKey: 'BG9uZS10aW1lLXByZWtleQ==',
  },
  pqLastResortPreKey: {
    id: 3,
    publicKey: 'BHBxLWxhc3QtcmVzb3J0LWtleQ==',
    signature: 'cHEtc2lnbmF0dXJl',
  },
};

export const prekeyBundleResponse: PreKeyBundleResponse = {
  bundle: prekeyBundle,
};

export const relayClientSend: RelayClientMessage = {
  kind: 'send',
  envelope,
};

export const relayClientAck: RelayClientMessage = {
  kind: 'ack',
  envelopeId: 'env-001',
};

export const relayClientFetchPending: RelayClientMessage = {
  kind: 'fetch_pending',
};

export const relayServerEnvelope: RelayServerMessage = {
  kind: 'envelope',
  envelope,
};

export const relayServerAckOk: RelayServerMessage = {
  kind: 'ack_ok',
  envelopeId: 'env-001',
};

export const relayServerPending: RelayServerMessage = {
  kind: 'pending',
  envelopes: [envelope],
};

export const relayServerError: RelayServerMessage = {
  kind: 'error',
  code: 'BAD_MESSAGE',
  message: 'unknown kind',
};

/** Filename → fixture object, written to shared/protocol/fixtures/. */
export const FIXTURES: Record<string, unknown> = {
  'envelope.json': envelope,
  'prekey-bundle.json': prekeyBundle,
  'prekey-bundle-response.json': prekeyBundleResponse,
  'relay-client-send.json': relayClientSend,
  'relay-client-ack.json': relayClientAck,
  'relay-client-fetch-pending.json': relayClientFetchPending,
  'relay-server-envelope.json': relayServerEnvelope,
  'relay-server-ack-ok.json': relayServerAckOk,
  'relay-server-pending.json': relayServerPending,
  'relay-server-error.json': relayServerError,
};
