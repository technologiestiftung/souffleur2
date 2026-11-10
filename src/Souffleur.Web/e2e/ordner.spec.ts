import { expect, test, type Page } from '@playwright/test';

const ADMIN = { email: 'admin@localhost', password: 'Entwicklung1234' };

/**
 * Die Ordner der Ablage (ADR-0013). Im Browsertest ist keine Synology
 * eingehängt; souffleur arbeitet dann mit der Ablage im eigenen
 * Datenverzeichnis -- derselbe Weg wie auf den Testinstanzen.
 *
 * Der Ordner wird nicht Standard: Die anderen Tests laden parallel hoch und
 * sollen weiter in souffleurs eigener Ablage landen.
 */
async function signIn(page: Page): Promise<void> {
  await page.goto('/anmelden');
  await page.getByLabel('E-Mail-Adresse').fill(ADMIN.email);
  await page.getByLabel('Passwort').fill(ADMIN.password);
  await page.getByRole('button', { name: 'Anmelden' }).click();
  await expect(page).not.toHaveURL(/anmelden/);
}

test('ein Ordner wird eingetragen, nimmt ein Dokument auf und wird wieder ausgetragen', async ({ page }) => {
  await signIn(page);
  await page.goto('/einstellungen/ordner');
  await expect(page.getByRole('heading', { name: 'Ordner der Ablage' })).toBeVisible();

  const name = `E2E ${Date.now()}`;
  await page.getByRole('button', { name: 'Ordner hinzufügen' }).click();
  const fenster = page.getByRole('dialog', { name: 'Ordner hinzufügen' });

  // Im Auswähler einen Unterordner anlegen -- er ist danach gewählt.
  await fenster.getByLabel('Name des neuen Unterordners').fill(name);
  await fenster.getByRole('button', { name: 'anlegen' }).click();
  await expect(fenster.getByText(`Gewählt: ${name}`)).toBeVisible();

  await fenster.getByLabel('Modul', { exact: true }).selectOption({ label: 'Produktionen' });
  await fenster.getByText(/Standardordner des Moduls/).click();
  await fenster.getByRole('button', { name: 'Speichern und einlesen' }).click();
  await expect(fenster).toHaveCount(0);

  const zeile = page.getByRole('table', { name: 'Ordner' }).getByRole('row').filter({ hasText: name });
  await expect(zeile).toContainText('Produktionen');
  await expect(zeile).not.toContainText('Standard');

  // In der Ablage: hochladen in genau diesen Ordner.
  await page.goto('/dokumente');
  const titel = `Probenplan ${Date.now()}`;
  await page.getByLabel('Dokumentdatei').setInputFiles({
    name: 'probenplan.pdf',
    mimeType: 'application/pdf',
    buffer: Buffer.from(`%PDF-1.4 ${titel}`),
  });
  const ablegen = page.getByRole('dialog', { name: 'Dokument ablegen' });
  await ablegen.getByLabel('Ablegen in').selectOption({ label: `${name} — ${name}` });
  await ablegen.getByLabel('Name').fill(titel);
  await ablegen.getByRole('button', { name: 'Ablegen' }).click();
  await expect(ablegen).toHaveCount(0);

  // Die Zeile zeigt, wo die Datei auf der Synology liegt.
  const doc = page.getByRole('row').filter({ hasText: titel });
  await expect(doc).toContainText(`${name}/probenplan.pdf`);

  // Aufräumen: Ordner austragen. Die Datei bleibt im Datenverzeichnis des
  // Testlaufs, das danach ohnehin verschwindet.
  await page.goto('/einstellungen/ordner');
  await page.getByRole('table', { name: 'Ordner' }).getByRole('button', { name, exact: true }).click();
  await page.getByRole('dialog', { name: 'Ordner ändern' }).getByRole('button', { name: 'Austragen' }).click();
  await expect(page.getByRole('table', { name: 'Ordner' }).getByRole('row').filter({ hasText: name })).toHaveCount(0);
});
