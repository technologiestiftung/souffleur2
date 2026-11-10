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

/**
 * Legt die Zeit auf den frühen Morgen. Im Beispielbestand ist dann nichts --
 * sonst hinge der Test davon ab, welcher Wochentag gerade ist und wer da im
 * Spielplan steht.
 */
async function earlyMorning(page: Page): Promise<void> {
  const dialog = page.locator('souffleur-event-dialog');
  await dialog.getByLabel('Von').fill('08:00');
  await dialog.getByLabel('Bis').fill('09:00');
}

/** Der heutige Tag als JJJJ-MM-TT in Ortszeit -- der Termin entsteht am Ankertag. */
function heute(): string {
  const now = new Date();
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

/** Teilt die Person ein, deren Name im Auswahlfeld vorkommt. */
async function assign(page: Page, surname: string): Promise<void> {
  const picker = page.locator('souffleur-event-dialog').getByLabel('Person einteilen');
  const options = await picker.locator('option').allTextContents();
  const index = options.findIndex((text) => text.includes(surname));

  expect(index, `„${surname}" steht nicht zur Auswahl: ${options.join(' | ')}`).toBeGreaterThan(0);

  await picker.selectOption({ index });
}

test('die Wortmarke kennzeichnet Vorführdaten', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);

  // Erfundene Termine sehen wie echte aus, sobald sie den Kalender verlassen.
  // Die Kennzeichnung hängt an der Wortmarke -- kein eigenes Schild, das den
  // Reitern die Breite nimmt.
  await expect(page.getByTitle(/In dieser Installation stehen erfundene Daten\./)).toContainText(
    'souffleur',
  );

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('jemand lässt sich einteilen und steht danach am Termin', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await page.goto('/kalender');

  const dialog = page.locator('souffleur-event-dialog');
  const title = `Besetzungsprobe ${Date.now()}`;

  await page.getByRole('button', { name: 'Termin anlegen', exact: true }).first().click();
  await dialog.getByLabel('Titel').fill(title);
  await earlyMorning(page);
  await assign(page, 'Fischer');

  await expect(dialog.getByLabel(/Funktion von Fischer/)).toBeVisible();
  await dialog.getByLabel(/Zusage von Fischer/).selectOption({ label: 'Zugesagt' });

  await dialog.getByRole('button', { name: 'Anlegen', exact: true }).click();
  await expect(dialog).toHaveCount(0);

  // Im Tagesfenster steht danach, wer dabei ist. Dazu den heutigen Tag im
  // Raster öffnen -- nach dem Speichern geht kein Fenster von selbst auf.
  // An der Zeile des Termins geprüft: Der Name steht auch in der versteckten
  // Auswahl des Filters.
  await page.getByRole('button', { name: `Tag ${heute()} zeigen` }).click();
  const row = page.getByRole('dialog').getByRole('listitem').filter({ hasText: title });
  await expect(row).toContainText('Fischer, David');

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('dieselbe Person zweimal zur selben Zeit wird gemeldet', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await page.goto('/kalender');

  const dialog = page.locator('souffleur-event-dialog');

  await page.getByRole('button', { name: 'Termin anlegen', exact: true }).first().click();
  await dialog.getByLabel('Titel').fill('Erster Dienst');
  await earlyMorning(page);
  await assign(page, 'Schröder');
  await dialog.getByRole('button', { name: 'Anlegen', exact: true }).click();
  await expect(dialog).toHaveCount(0);

  await page.getByRole('button', { name: 'Termin anlegen', exact: true }).first().click();
  await dialog.getByLabel('Titel').fill('Zweiter Dienst');
  await earlyMorning(page);
  await assign(page, 'Schröder');
  await dialog.getByRole('button', { name: 'Anlegen', exact: true }).click();

  // Niemand ist an zwei Orten -- dieselbe Meldung wie bei einer Bühne.
  await expect(dialog.getByText('Schon vergeben')).toBeVisible();

  // Im Kasten steht, womit es kollidiert -- der Name steht auch oben in der
  // Besetzung, deshalb hier über den Titel des Gegentermins geprüft.
  await expect(dialog.getByText('Erster Dienst')).toBeVisible();

  // Und wer absagt, ist wieder frei.
  await dialog.getByLabel(/Zusage von Schröder/).selectOption({ label: 'Abgesagt' });
  await dialog.getByRole('button', { name: 'Anlegen', exact: true }).click();
  await expect(dialog).toHaveCount(0);

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('der Personenfilter zeigt nur deren Termine', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await page.goto('/kalender');

  const filter = page.getByRole('main').getByRole('combobox', { name: 'Person' });
  await filter.selectOption({ label: 'Behrens, Anja' });

  // Anja Behrens führt Regie bei den Hamlet-Proben, spielt aber nicht in
  // „Der Besuch der alten Dame".
  await expect(page.getByTitle(/Probe Hamlet/).first()).toBeVisible();
  await expect(page.getByTitle(/Der Besuch der alten Dame/)).toHaveCount(0);

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
  expect(await filter.count()).toBeGreaterThan(0);
});
