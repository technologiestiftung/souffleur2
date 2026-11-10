import { defineConfig, devices } from '@playwright/test';
import { createHash } from 'node:crypto';
import { tmpdir } from 'node:os';
import { basename, join, resolve } from 'node:path';

/**
 * Rauchtests gegen die laufende Oberfläche.
 *
 * Es gibt sie, weil ein Startfehler in Angular nur eine weiße Seite erzeugt:
 * Der Server liefert HTTP 200, die Datei ist vollständig, und trotzdem sieht
 * niemand etwas. Kein Test auf dem Server hätte das gefunden.
 *
 * Gestartet wird eine eigene Instanz mit eigener Datenbank -- die Tests
 * schreiben Nachrichten und legen Kontakte an, und das gehört nicht in die
 * Umgebung, in der gearbeitet wird. Nach dem Lauf ist sie wieder weg.
 *
 * Port und Datenverzeichnis hängen an der Arbeitskopie: Zwei Sitzungen in
 * zwei Worktrees bekommen zwei Server. Mit einem festen Port und einem
 * gemeinsamen Verzeichnis räumte der eine Lauf dem anderen die Datenbank
 * unter dem Server weg („attempt to write a readonly database“).
 */
const root = resolve(__dirname, '..', '..');
const digest = createHash('sha1').update(root).digest('hex');
const port = 5100 + (parseInt(digest.slice(0, 6), 16) % 400);
const dataDirectory = join(tmpdir(), `souffleur-e2e-${basename(root)}-${digest.slice(0, 8)}`);

export default defineConfig({
  testDir: './e2e',
  timeout: 30_000,
  retries: 0,
  reporter: [['list']],
  use: {
    baseURL: `http://localhost:${port}`,
    locale: 'de-DE',
    timezoneId: 'Europe/Berlin',
  },
  webServer: {
    command: './e2e-server.sh',
    url: `http://localhost:${port}/api/health`,
    timeout: 180_000,
    reuseExistingServer: false,
    stdout: 'ignore',
    stderr: 'pipe',
    env: {
      SOUFFLEUR_E2E_PORT: String(port),
      SOUFFLEUR_E2E_DATA: dataDirectory,
    },
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
