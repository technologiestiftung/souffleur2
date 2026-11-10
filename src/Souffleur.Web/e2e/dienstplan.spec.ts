import { expect, test, type Page } from '@playwright/test';

const ADMIN = { email: 'admin@localhost', password: 'Entwicklung1234' };

/**
 * Der Dienstplan rechnet gegen den Kalender: Der Bedarf hängt an der
 * Terminkategorie, und eine Vorstellung ohne jede Einteilung taucht deshalb
 * auf -- in einer Liste der Eingeteilten wäre sie unsichtbar.
 */
function collectErrors(page: Page): string[] {
  const expected = /Failed to load resource.*(400|401|403|404|409)/;
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

test('der Dienstplan zeigt, wo jemand fehlt', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await page.goto('/personal');

  // Der Bedarf steht an der Terminart, also erscheinen auch Vorstellungen,
  // an denen noch niemand eingeteilt ist.
  await expect(page.getByText(/Dienste sind bis .* nicht besetzt/)).toBeVisible();
  await expect(page.getByText(/Technik \d+\/\d+/).first()).toBeVisible();

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('der Bedarf einer Terminart lässt sich setzen und wieder entfernen', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await page.goto('/einstellungen/dienstbedarf');

  // Für „Sitzung" gibt es noch keinen Bedarf.
  await page.getByLabel('Terminart').selectOption({ label: 'Sitzung' });
  await page.getByRole('button', { name: 'Bedarf anlegen' }).click();

  await page.getByLabel('Funktion').selectOption({ label: 'Inspizienz' });
  await page.getByLabel('Anzahl').fill('2');

  // Die Vorführdaten bringen Gruppen mit, und der Bogen sagt, wozu sie hier gut sind.
  await page.getByLabel('Vorschlag aus Gruppe').selectOption({ label: 'Inspizienz' });
  await expect(page.getByText(/engt ein, wen der Dienstplan/)).toBeVisible();

  await page.getByRole('button', { name: 'Speichern' }).click();

  const row = page.getByRole('listitem').filter({ hasText: 'Sitzung' });
  await expect(row.getByText('2 × Inspizienz')).toBeVisible();

  // Und wieder weg -- der Termin selbst bleibt davon unberührt.
  await row.getByRole('button', { name: /entfernen/ }).click();
  await expect(page.getByRole('listitem').filter({ hasText: 'Sitzung' })).toHaveCount(0);

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('eine offene Stelle lässt sich direkt aus dem Plan besetzen', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await page.goto('/personal');

  // Die erste offene Stelle, die sich besetzen lässt. Serienvorkommen
  // tragen keinen eigenen Termin und bekommen deshalb keinen Knopf.
  const besetzen = page.getByRole('button', { name: /besetzen$/ }).first();
  await expect(besetzen).toBeVisible();
  await besetzen.click();

  // Ausdruecklich jemanden waehlen, der zu der Zeit frei ist -- sonst
  // antwortet der Server mit einer Doppelbelegung, und das prueft ein
  // anderer Test.
  const auswahl = page.getByLabel('Wer');
  const wert = await auswahl
    .locator('option')
    .filter({ hasText: 'Fischer, David' })
    .getAttribute('value');
  await auswahl.selectOption(wert!);
  await page.getByRole('button', { name: 'Einteilen', exact: true }).click();

  // Danach steht eine Person mehr im Plan, und der Zähler ist gewandert.
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(page.getByText('Eingeteilt').first()).toBeVisible();

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});
