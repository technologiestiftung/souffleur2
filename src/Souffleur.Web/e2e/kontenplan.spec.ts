import { expect, test, type Page } from '@playwright/test';

const ADMIN = { email: 'admin@localhost', password: 'Entwicklung1234' };

async function signIn(page: Page): Promise<void> {
  await page.goto('/anmelden');
  await page.getByLabel('E-Mail-Adresse').fill(ADMIN.email);
  await page.getByLabel('Passwort').fill(ADMIN.password);
  await page.getByRole('button', { name: 'Anmelden' }).click();
  await expect(page).toHaveURL(/uebersicht/);
}

/** Nummern, die es sonst nicht gibt -- die Tests laufen gegen eine gemeinsame Datenbank. */
function nummer(): string {
  return String(70000000 + Math.floor(Math.random() * 9999999));
}

test('die Kontenliste vom Steuerbüro lässt sich mit Vorschau einlesen', async ({ page }) => {
  await signIn(page);
  const [mitArt, ohneArt] = [nummer(), nummer()];

  await page.goto('/einstellungen/kontenplan');
  await page.getByLabel('Kontenliste wählen').setInputFiles({
    name: 'konten.csv',
    mimeType: 'text/csv',
    buffer: Buffer.from(
      [
        'Nummer;Name;Art;Steuerschlüssel',
        `${mitArt};Gagen Gastspiel;Aufwand;`,
        `${ohneArt};Erlöse Abendkasse;;`,
        ';Zeile ohne Nummer;Aufwand;',
      ].join('\n'),
    ),
  });

  // Erst die Vorschau -- nichts ist angelegt, bevor jemand es will.
  await expect(page.getByText('2 neu, 1 werden übergangen')).toBeVisible();
  await expect(page.getByText('ohne Nummer oder Name')).toBeVisible();
  const einlesen = page.getByRole('button', { name: /Konten einlesen/ });
  await expect(einlesen).toBeDisabled();

  // Die fehlende Art wird gewählt, nicht geraten.
  await page.getByLabel(`Art von ${ohneArt}`).selectOption({ label: 'Erlös' });
  await einlesen.click();
  await expect(page.getByRole('status')).toContainText('2 Konten eingelesen');

  const konten = page.getByRole('table', { name: 'Konten' });
  await expect(konten.getByRole('row').filter({ hasText: ohneArt })).toContainText('Erlös');
});

test('ein Konto lässt sich ändern und stilllegen, die Nummer bleibt', async ({ page }) => {
  await signIn(page);
  const nr = nummer();
  const angelegt = await page.request.post('/api/accounting/accounts', {
    data: { number: nr, name: 'Probenbedarf', type: 5 },
  });
  expect(angelegt.ok(), await angelegt.text()).toBeTruthy();

  await page.goto('/einstellungen/kontenplan');
  await page.getByRole('button', { name: `${nr} ändern` }).click();

  const bogen = page.getByRole('dialog', { name: `Konto ${nr} ändern` });
  await expect(bogen.getByLabel('Nummer')).toBeDisabled();
  await bogen.getByLabel('Name').fill('Probenbedarf und Requisite');
  await bogen.getByRole('button', { name: 'Speichern' }).click();
  await expect(bogen).toHaveCount(0);

  const konten = page.getByRole('table', { name: 'Konten' });
  await expect(konten.getByText('Probenbedarf und Requisite')).toBeVisible();

  // Stillgelegt verschwindet es aus der Liste, bleibt aber erhalten.
  await page.getByRole('button', { name: `${nr} stilllegen` }).click();
  await expect(konten.getByText('Probenbedarf und Requisite')).toHaveCount(0);
  await page.getByLabel(/auch stillgelegte/).check();
  await expect(konten.getByRole('row').filter({ hasText: nr })).toContainText('stillgelegt');
});
