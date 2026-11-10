import { expect, test, type Page } from '@playwright/test';

const ADMIN = { email: 'admin@localhost', password: 'Entwicklung1234' };

/**
 * Die eigenen Zeiten: kommen, gehen, nachtragen.
 *
 * Gestempelt wird auf den Kontakt, nicht auf das Konto -- in den Vorführdaten
 * ist das Konto mit „Krause, Anna" verbunden, damit die Vorführung nicht an
 * diesem Schritt hängenbleibt.
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

async function openTimes(page: Page): Promise<void> {
  await page.goto('/personal');
  await page.getByRole('button', { name: 'Zeiten' }).click();
}

test('kommen und gehen ergeben eine Zeit in der Woche', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await openTimes(page);

  await page.getByRole('button', { name: 'Kommen stempeln' }).click();

  // Die laufende Zeit steht da, mit ihrem Beginn.
  await expect(page.getByText(/Läuft seit \d{2}:\d{2} Uhr/)).toBeVisible();

  await page.getByLabel('Pause in Minuten').fill('0');
  await page.getByRole('button', { name: 'Gehen stempeln' }).click();

  // Danach ist nichts mehr offen, und der Tag trägt einen Eintrag.
  await expect(page.getByRole('button', { name: 'Kommen stempeln' })).toBeVisible();
  await expect(page.getByText('Gestempelt').first()).toBeVisible();

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('eine Korrektur ohne Grund wird abgewiesen', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await openTimes(page);

  // Erst etwas zum Korrigieren. Die Nachtzeit ist bewusst gewählt: Sie
  // überschneidet sich weder mit den Vorführdaten noch mit dem Stempeln
  // im anderen Test -- und zwei Zeiten derselben Person dürfen sich nicht
  // überschneiden.
  await page.getByRole('button', { name: 'Zeit nachtragen' }).click();
  await page.getByLabel('von').fill('02:00');
  await page.getByLabel('bis').fill('03:00');
  await page.getByLabel('Pause in Minuten').last().fill('0');
  await page.getByLabel('Notiz').fill('Nachtprobe');
  await page.getByRole('button', { name: 'Speichern' }).click();

  // Der Tag ist selbst ein Listenpunkt und enthaelt den Eintrag --
  // gemeint ist der innere.
  const row = page.getByRole('listitem').filter({ hasText: 'Nachtprobe' }).last();
  await expect(row).toBeVisible();
  await expect(row.getByText('Nachgetragen')).toBeVisible();

  // Richtigstellen geht nur mit Grund -- der Server besteht darauf.
  await row.getByRole('button', { name: 'richtigstellen' }).click();
  await page.getByLabel('bis').fill('04:00');
  await page.getByRole('button', { name: 'Speichern' }).click();
  await expect(page.getByText(/braucht einen Grund/)).toBeVisible();

  await page.getByLabel('Grund der Korrektur').fill('Ende vergessen');
  await page.getByRole('button', { name: 'Speichern' }).click();

  await expect(page.getByText('Richtiggestellt: Ende vergessen')).toBeVisible();

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('ein Konto ohne Kontakt bekommt gesagt, was fehlt', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await page.goto('/kontakte');

  // Umgekehrter Weg: Die Verwaltung sieht am Kontakt, womit er verbunden ist.
  // Den Knopf auf der Seite, nicht den gleichnamigen in der Aktionsleiste.
  await page.getByRole('main').getByRole('button', { name: 'Kontakt anlegen' }).click();
  await page.getByRole('button', { name: 'Person', exact: true }).click();
  await page.getByRole('button', { name: 'Haus', exact: true }).click();

  await expect(page.getByLabel('Benutzerkonto')).toBeVisible();

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('die Künstlersozialabgabe zählt zusammen und zeigt, was offen ist', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await page.goto('/personal');
  await page.getByRole('button', { name: 'Künstlersozialabgabe' }).click();

  // Ohne hinterlegten Satz gibt es eine Grundlage, aber keine Abgabe.
  await expect(page.getByText(/kein Abgabesatz hinterlegt/)).toBeVisible();

  const flagged = page.getByRole('row').filter({ hasText: 'Weber, Clara' });
  await expect(flagged).toBeVisible();

  // Wer künstlerisch arbeitet und nicht gekennzeichnet ist, fällt auf.
  await expect(page.getByText('Nicht gekennzeichnet — bitte prüfen')).toBeVisible();
  await expect(page.getByText('Marquardt, Jens')).toBeVisible();

  // Mit Satz erscheint die Abgabe.
  await page.getByLabel('Abgabesatz in Prozent').fill('5');
  await page.getByRole('button', { name: 'übernehmen' }).click();
  await expect(page.getByText(/kein Abgabesatz hinterlegt/)).toHaveCount(0);

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});
