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
 * Das Suchfeld oben: tippen, Fund anklicken, dort landen. Die Vorführdaten
 * liefern die Ziele -- Anna Krause im Adressbuch, „Premiere Hamlet
 * vorbereiten" bei den Aufgaben.
 */
test('wer einen Kontakt sucht, landet bei seinem Bogen', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);

  const feld = page.getByRole('searchbox', { name: 'Suchen in souffleur' });
  await feld.fill('krause');

  const liste = page.getByRole('listbox', { name: 'Suchergebnisse' });
  await expect(liste).toBeVisible();

  // Nach Bereichen gebündelt: Der Fund steht unter „Kontakte".
  const kontakte = liste.getByRole('group', { name: 'Kontakte' });
  const anna = kontakte.getByRole('option', { name: /Krause, Anna/ });
  await expect(anna).toBeVisible();

  await anna.click();
  await expect(page).toHaveURL(/\/kontakte\?kontakt=/);
  await expect(page.getByRole('dialog', { name: 'Krause, Anna ändern' })).toBeVisible();

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('mit der Tastatur: tippen, Pfeil, Enter -- und die Aufgabe ist offen', async ({ page }) => {
  await signIn(page);

  const feld = page.getByRole('searchbox', { name: 'Suchen in souffleur' });
  await feld.fill('hamlet vorbereiten');

  const liste = page.getByRole('listbox', { name: 'Suchergebnisse' });
  const aufgabe = liste
    .getByRole('group', { name: 'Aufgaben' })
    .getByRole('option', { name: /Premiere Hamlet vorbereiten/ });
  await expect(aufgabe).toBeVisible();

  // Die erste Zeile ist vorgewählt; einmal vor und zurück prüft das Wandern,
  // dann öffnet Enter.
  await feld.press('ArrowDown');
  await feld.press('ArrowUp');
  await expect(aufgabe).toHaveAttribute('aria-selected', 'true');
  await feld.press('Enter');

  await expect(page).toHaveURL(/\/aufgaben\?aufgabe=/);
  await expect(page.getByRole('dialog', { name: 'Aufgabe ändern' })).toBeVisible();
  await expect(page.getByLabel('Titel')).toHaveValue('Premiere Hamlet vorbereiten');

  // Nach dem Sprung ist die Liste zu.
  await expect(liste).toBeHidden();
});

test('die Suche sagt, was sie nicht findet, und Strg+K führt ins Feld', async ({ page }) => {
  await signIn(page);

  await page.keyboard.press('ControlOrMeta+k');
  const feld = page.getByRole('searchbox', { name: 'Suchen in souffleur' });
  await expect(feld).toBeFocused();

  await feld.fill('xyzzy-gibt-es-nicht');
  await expect(page.getByText(/Nichts gefunden zu „xyzzy-gibt-es-nicht“/)).toBeVisible();

  // Ein Zeichen reicht noch nicht -- dann bleibt die Liste zu.
  await feld.fill('x');
  await expect(page.getByRole('listbox', { name: 'Suchergebnisse' })).toBeHidden();
});
