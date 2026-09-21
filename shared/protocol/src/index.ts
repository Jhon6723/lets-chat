/**
 * Shared wire-protocol types for Let's Chat.
 *
 * Single source of truth for the E2EE envelope and prekey formats consumed by
 * both the web client (frontend/) and the relay backend (backend/) — ADR-0001.
 * The server only ever relays these structures; it never sees plaintext.
 */

export const PROTOCOL_VERSION = 1 as const;

/** A Signal-style address: userId.deviceId */
export type DeviceAddress = `${string}.${number}`;

export type EnvelopeType = 'prekey_bundle' | 'message';

/** Opaque encrypted envelope the relay stores and forwards. */
export interface EncryptedEnvelope {
  version: typeof PROTOCOL_VERSION;
  id: string;
  senderAddress: DeviceAddress;
  recipientAddress: DeviceAddress;
  type: EnvelopeType;
  /** base64-encoded ciphertext produced by the Signal session */
  ciphertext: string;
  /** base64-encoded ratchet/header bytes needed by the recipient */
  header: string;
  /** unix ms when the sender created the envelope */
  createdAt: number;
}

/** Public prekey material published by a device so senders can open a session. */
export interface PreKeyBundle {
  address: DeviceAddress;
  identityKey: string;
  signedPreKey: {
    id: number;
    publicKey: string;
    signature: string;
  };
  oneTimePreKey?: {
    id: number;
    publicKey: string;
  };
  /** post-quantum (SPQR/PQXDH) last-resort key, if supported by the client */
  pqLastResortPreKey?: {
    id: number;
    publicKey: string;
    signature: string;
  };
}

/** REST payload for fetching a contact's prekey bundle. */
export interface PreKeyBundleResponse {
  bundle: PreKeyBundle;
}

/** WSS message shapes between client and relay. */
export type RelayClientMessage =
  | { kind: 'send'; envelope: EncryptedEnvelope }
  | { kind: 'ack'; envelopeId: string }
  | { kind: 'fetch_pending' }
  /**
   * Device-signature authentication (ADR-0007): client answers the server
   * nonce challenge by signing "relay-auth:{nonce}" with the device identity
   * private key. Required before any relay operation.
   */
  | { kind: 'auth'; address: DeviceAddress; signature: string };

export type RelayServerMessage =
  | { kind: 'envelope'; envelope: EncryptedEnvelope }
  | { kind: 'ack_ok'; envelopeId: string }
  | { kind: 'pending'; envelopes: EncryptedEnvelope[] }
  | { kind: 'error'; code: string; message: string }
  /** First frame on every connection: the nonce the client must sign. */
  | { kind: 'auth_challenge'; nonce: string }
  /** Authentication accepted — connection is bound to this device address. */
  | { kind: 'auth_ok'; address: DeviceAddress };
