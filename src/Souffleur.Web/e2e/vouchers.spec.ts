import { expect, test, type Page } from '@playwright/test';

const ADMIN = { email: 'admin@localhost', password: 'Entwicklung1234' };

/**
 * @param also Ein weiterer Statuscode, der in diesem Test dazugehört. Eine abgewiesene
 * Eingabe erzeugt eine 400 und damit einen Eintrag in der Browserkonsole --
 * das ist hier kein Fehler, sondern das Geprüfte.
 */
function collectErrors(page: Page, also?: string): string[] {
  const allowed = ['401', '403', '404', '409', ...(also ? [also] : [])].join('|');
  const expected = new RegExp(`Failed to load resource.*(${allowed})`);
  const errors: string[] = [];

  page.on('console', (message) => {
    if (message.type() === 'error' && !expected.test(message.text())) {
      errors.push(message.text());
    }
  });
  page.on('pageerror', (error) => errors.push(error.message));
  return errors;
}

async function signIn(page: Page): Promise<void> {
  await page.goto('/anmelden');
  await page.getByLabel('E-Mail-Adresse').fill(ADMIN.email);
  await page.getByLabel('Passwort').fill(ADMIN.password);
  await page.getByRole('button', { name: 'Anmelden' }).click();
  await expect(page).toHaveURL(/uebersicht/);
}

test('die Belegliste zeigt, was vorliegt', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);

  await page.goto('/finanzen/belege');

  await expect(page.getByText('Beleuchtungsmittel, Nachbestellung')).toBeVisible();
  await expect(page.getByText('Bühnenbedarf Schmidt GmbH')).toBeVisible();

  // Was noch keiner Buchung zugeordnet ist, bietet den Weg dorthin an.
  await expect(page.getByRole('button', { name: 'mit Buchung verbinden' }).first()).toBeVisible();

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('ein Beleg lässt sich mit Datei ablegen und wieder öffnen', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await page.goto('/finanzen/belege');

  const description = `Kassenbon ${Date.now()}`;

  await page.getByRole('button', { name: 'Beleg hinzufügen' }).first().click();
  // Eine E-Rechnung, wie sie wirklich käme -- beliebige Dateitypen nimmt
  // der Server nicht an.
  await page.getByLabel('Belegdatei').setInputFiles({
    name: 'rechnung.xml',
    mimeType: 'text/xml',
    buffer: Buffer.from('<?xml version="1.0"?><Invoice><Total>12.50</Total></Invoice>'),
  });
  await page.getByLabel('Betrag brutto').fill('12.50');
  await page.getByLabel('Beschreibung').fill(description);
  await page.getByRole('button', { name: 'Beleg ablegen' }).click();

  const row = page.getByRole('row').filter({ hasText: description });
  await expect(row).toBeVisible();

  // Die Datei liegt dahinter und ist abrufbar.
  const link = row.getByRole('link', { name: 'öffnen' });
  await expect(link).toBeVisible();

  const href = await link.getAttribute('href');
  const file = await page.request.get(href!);
  expect(file.status()).toBe(200);
  expect(await file.text()).toContain('<Total>12.50</Total>');

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('ein Beleg lässt sich auf die Fläche fallen lassen', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await page.goto('/finanzen/belege');

  const description = `Gezogener Beleg ${Date.now()}`;

  await page.getByRole('button', { name: 'Beleg hinzufügen' }).first().click();

  // Der kürzere Weg: Die Datei kommt nicht aus der Dateiauswahl, sondern
  // fällt auf die Fläche. Genau so kommt sie aus dem Mailanhang.
  const dropped = await page.evaluateHandle(() => {
    const transfer = new DataTransfer();
    transfer.items.add(
      new File(['<?xml version="1.0"?><Invoice><Total>8.00</Total></Invoice>'], 'gezogen.xml', {
        type: 'text/xml',
      }),
    );
    return transfer;
  });

  // Die Fläche selbst ist das Kind der Komponente -- dort hängt der Empfang.
  await page
    .locator('souffleur-drop-zone > div')
    .dispatchEvent('drop', { dataTransfer: dropped });

  await expect(page.getByText('gezogen.xml')).toBeVisible();

  await page.getByLabel('Betrag brutto').fill('8.00');
  await page.getByLabel('Beschreibung').fill(description);
  await page.getByRole('button', { name: 'Beleg ablegen' }).click();

  const row = page.getByRole('row').filter({ hasText: description });
  await expect(row.getByRole('link', { name: 'öffnen' })).toBeVisible();

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('der Prüflauf rechnet die Prüfwerte nach', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await page.goto('/finanzen/belege');

  await page.getByRole('button', { name: 'Dateien prüfen' }).click();

  // Nichts ist angefasst worden, also muss alles stimmen.
  await expect(
    page.getByText('Alle hinterlegten Dateien stimmen mit ihrem Prüfwert überein.'),
  ).toBeVisible();

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('ein Dateityp, der nicht als Beleg taugt, wird abgewiesen', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await page.goto('/finanzen/belege');

  await page.getByRole('button', { name: 'Beleg hinzufügen' }).first().click();
  await page.getByLabel('Belegdatei').setInputFiles({
    name: 'notiz.txt',
    mimeType: 'text/plain',
    buffer: Buffer.from('nur eine Notiz'),
  });

  // Abgewiesen wird hier, nicht erst nach dem Hochladen -- der Grund steht dabei.
  await expect(page.getByText(/notiz\.txt ist hier nicht vorgesehen/)).toBeVisible();

  // Und aus der leeren Fläche entsteht kein Beleg ohne Beleg.
  await page.getByLabel('Betrag brutto').fill('5.00');
  await page.getByLabel('Beschreibung').fill('Sollte nicht durchgehen');
  await page.getByRole('button', { name: 'Beleg ablegen' }).click();
  await expect(page.getByText(/keine Datei gewählt/)).toBeVisible();

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('eine Rechnungsnummer lässt sich nachtragen', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await page.goto('/finanzen/belege');

  const row = page.getByRole('row').filter({ hasText: 'Fahrtkosten Gastregie' });
  await row.getByRole('button', { name: /richtigstellen/ }).click();

  await page.getByLabel('Rechnungsnummer').fill('RE-2026-0999');
  await page.getByRole('button', { name: 'Richtigstellen', exact: true }).click();

  await expect(row).toContainText('RE-2026-0999');

  // Die Belegnummer bleibt, was sie war -- sonst entstünde eine Lücke.
  await expect(row).toContainText('2026-00002');

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('ein Beleg wechselt das Geschäftsjahr nicht', async ({ page }) => {
  const errors = collectErrors(page, '400');
  await signIn(page);
  await page.goto('/finanzen/belege');

  const row = page.getByRole('row').filter({ hasText: 'Beleuchtungsmittel' });
  await row.getByRole('button', { name: /richtigstellen/ }).click();

  await page.getByLabel('Belegdatum').fill('2025-12-30');
  await page.getByRole('button', { name: 'Richtigstellen', exact: true }).click();

  await expect(page.getByText(/Geschaeftsjahr 2026 bleiben/)).toBeVisible();

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});
