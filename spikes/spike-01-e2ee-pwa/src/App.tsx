import { useState } from 'react';
import './App.css';
import { runBenchmark, verifyReloadSurvival, type MetricResult } from './spike/benchmark';

function App() {
  const [metrics, setMetrics] = useState<MetricResult[]>([]);
  const [logLines, setLogLines] = useState<string[]>([]);
  const [running, setRunning] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [pendingReload, setPendingReload] = useState(
    () => localStorage.getItem('spike.persist.pending') === 'true'
  );

  const appendLog = (line: string) => setLogLines((prev) => [...prev, line]);

  async function run(fn: typeof runBenchmark) {
    setRunning(true);
    setError(null);
    setMetrics([]);
    setLogLines([]);
    try {
      const out = await fn(appendLog);
      setMetrics(out.metrics);
      setPendingReload(localStorage.getItem('spike.persist.pending') === 'true');
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setRunning(false);
    }
  }

  return (
    <main className="spike">
      <h1>Spike 01 — E2EE in a PWA</h1>
      <p>
        Signal Protocol via <code>@open-e2ee/signal-protocol-sdk</code>. Alice uses an in-memory
        store; Bob persists to IndexedDB.
      </p>

      {!window.isSecureContext && (
        <pre className="error">
          Insecure context: this page is not served over HTTPS or localhost, so
          crypto.subtle is unavailable and the SDK cannot initialize.{'\n'}
          On Android either enable chrome://flags/#unsafely-treat-insecure-origin-as-secure
          for this origin, or use adb reverse tcp:5173 tcp:5173 and open
          http://localhost:5173 on the phone.
        </pre>
      )}

      <div className="actions">
        <button disabled={running || !window.isSecureContext} onClick={() => run(runBenchmark)}>
          {running ? 'Running…' : 'Run benchmark'}
        </button>
        <button disabled={running || !pendingReload || !window.isSecureContext} onClick={() => run(verifyReloadSurvival)}>
          Verify reload survival
        </button>
      </div>

      {error && <pre className="error">{error}</pre>}

      {metrics.length > 0 && (
        <table>
          <thead>
            <tr>
              <th>Criterion</th>
              <th>Result</th>
              <th>Threshold</th>
              <th>Verdict</th>
            </tr>
          </thead>
          <tbody>
            {metrics.map((m) => (
              <tr key={m.name}>
                <td>{m.name}</td>
                <td>{m.value}</td>
                <td>{m.threshold}</td>
                <td className={m.pass ? 'pass' : 'fail'}>{m.pass ? 'PASS' : 'FAIL'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <h2>Log</h2>
      <pre className="log">
        {logLines.map((l, i) => (
          <div key={i}>{l}</div>
        ))}
      </pre>
    </main>
  );
}

export default App;
