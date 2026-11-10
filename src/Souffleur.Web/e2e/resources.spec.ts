import { expect, test, type Page } from '@playwright/test';

const ADMIN = { email: 'admin@localhost', password: 'Entwicklung1234' };

function collectErrors(page: Page): string[] {
  const expected = /Failed to load resource.*(401|403|404|409)/;
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

/** Eine eigene Bühne je Test -- sonst stolpern die Tests übereinander. */
async function addStage(page: Page, name: string): Promise<void> {
  await page.goto('/einstellungen/ressourcen');
  await page.getByRole('button', { name: 'Ressource anlegen' }).first().click();
  await page.getByLabel('Name', { exact: true }).fill(name);
  await page.getByRole('button', { name: 'Speichern' }).click();
  await expect(page.getByRole('cell', { name, exact: true })).toBeVisible();
}

test('die Ressourcen des Hauses lassen sich verwalten', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);

  await page.goto('/einstellungen/ressourcen');

  // Die Beispieldaten bringen die Bühnen des Hauses mit.
  await expect(page.getByRole('cell', { name: 'Großer Saal', exact: true })).toBeVisible();

  // Der Lichtpark ist mehrfach vergebbar -- die Unterscheidung steht in der Liste.
  await expect(page.getByRole('row', { name: /Lichtpark/ })).toContainText('mehrfach');
  await expect(page.getByRole('row', { name: /Großer Saal/ })).toContainText('nur einmal');

  const name = `Studio ${Date.now()}`;
  await addStage(page, name);

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('eine belegte Ressource lässt sich nicht löschen', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await page.goto('/einstellungen/ressourcen');

  // Der Große Saal trägt die Vorstellungen -- löschen geht nicht, nur abschalten.
  const row = page.getByRole('row', { name: /Großer Saal/ });
  await expect(row.getByRole('button', { name: 'Löschen' })).toHaveCount(0);
  await expect(row.getByRole('button', { name: 'Abschalten' })).toBeVisible();

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

/** Der heutige Tag als JJJJ-MM-TT in Ortszeit -- der Termin entsteht am Ankertag. */
function heute(): string {
  const now = new Date();
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

test('eine Doppelbelegung wird gemeldet und lässt sich übergehen', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);

  const stage = `Saal ${Date.now()}`;
  await addStage(page, stage);

  await page.goto('/kalender');
  const dialog = page.locator('souffleur-event-dialog');

  // Erster Termin auf der neuen Bühne.
  await page.getByRole('button', { name: 'Termin anlegen', exact: true }).first().click();
  await dialog.getByLabel('Titel').fill('Erster Durchlauf');
  await dialog.getByRole('button', { name: stage, exact: true }).click();
  await dialog.getByRole('button', { name: 'Anlegen', exact: true }).click();
  await expect(dialog).toHaveCount(0);

  // Zweiter Termin, dieselbe Bühne, dieselbe Zeit.
  await page.getByRole('button', { name: 'Termin anlegen', exact: true }).first().click();
  await dialog.getByLabel('Titel').fill('Zweiter Durchlauf');
  await dialog.getByRole('button', { name: stage, exact: true }).click();
  await dialog.getByRole('button', { name: 'Anlegen', exact: true }).click();

  // Nicht abgelehnt, sondern angehalten: Der Grund steht da, und daneben der Weg.
  await expect(dialog.getByText('Schon vergeben')).toBeVisible();
  await expect(dialog.getByText('Erster Durchlauf')).toBeVisible();

  await dialog.getByRole('button', { name: 'Trotzdem anlegen' }).click();
  await expect(dialog).toHaveCount(0);

  // Der Termin steht danach im Tagesfenster -- das Raster kürzt volle Tage.
  await page.getByRole('button', { name: `Tag ${heute()} zeigen` }).click();
  await expect(page.getByRole('dialog').getByText('Zweiter Durchlauf')).toBeVisible();

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('vorab lässt sich fragen, ob die Bühne frei ist', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);

  const stage = `Probenraum ${Date.now()}`;
  await addStage(page, stage);

  await page.goto('/kalender');
  const dialog = page.locator('souffleur-event-dialog');

  await page.getByRole('button', { name: 'Termin anlegen', exact: true }).first().click();
  await dialog.getByLabel('Titel').fill('Noch nichts da');
  await dialog.getByRole('button', { name: stage, exact: true }).click();
  await dialog.getByRole('button', { name: 'Frei?' }).click();

  await expect(dialog.getByText('Nichts steht im Weg.')).toBeVisible();

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('der Belegungsfilter zeigt nur, was auf dieser Bühne liegt', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await page.goto('/kalender');

  const filter = page.getByRole('button', { name: 'Probebühne 1', exact: true });
  await expect(filter).toHaveAttribute('aria-pressed', 'false');

  await filter.click();
  await expect(filter).toHaveAttribute('aria-pressed', 'true');

  // Jetzt stehen nur noch die Proben da -- die Vorstellungen liegen im Saal.
  await expect(page.getByTitle(/Probe Hamlet/).first()).toBeVisible();
  await expect(page.getByTitle(/Der Besuch der alten Dame/)).toHaveCount(0);

  await page.getByRole('button', { name: 'alle zeigen' }).click();
  await expect(filter).toHaveAttribute('aria-pressed', 'false');

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});
