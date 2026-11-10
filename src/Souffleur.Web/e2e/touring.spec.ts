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

/** Ein Tag in n Tagen als ISO-Datum -- in der Ortszeit des Browsers. */
function inDays(days: number): string {
  const date = new Date();
  date.setDate(date.getDate() + days);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/** Schaltet Touring ein, falls es aus ist -- jeder Lauf beginnt mit einer frischen Datenbank. */
async function enableTouring(page: Page): Promise<void> {
  await page.goto('/einstellungen/module');
  const schalter = page.getByRole('listitem').filter({ hasText: 'Touring' }).getByRole('switch');
  if (!(await schalter.isChecked())) {
    await schalter.click();
  }
  await expect(schalter).toBeChecked();
}

/**
 * Eine Spielstätte ist ein Kontakt mit dieser Rolle -- es gibt kein zweites
 * Adressbuch im Touring. Angelegt über die Schnittstelle, weil der Test das
 * Touring prüft, nicht das Kontaktformular.
 */
async function venue(page: Page, name: string): Promise<void> {
  const response = await page.request.post('/api/contacts', {
    data: { kind: 1, name, city: 'Hamburg', roles: [7] },
  });
  expect(response.ok(), await response.text()).toBeTruthy();
}

test('Touring ist in einer frischen Installation aus und lässt sich einschalten', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);

  const reiter = page.getByRole('navigation', { name: 'Module' });
  await page.goto('/einstellungen/module');
  const schalter = page.getByRole('listitem').filter({ hasText: 'Touring' }).getByRole('switch');

  // Wer die Seite direkt aufruft, bekommt den Hinweis statt einer leeren Liste.
  if (!(await schalter.isChecked())) {
    await expect(reiter.getByRole('link', { name: 'Touring' })).toHaveCount(0);
    await page.goto('/touring');
    await expect(page.getByText('Touring ist in diesem Haus nicht eingeschaltet')).toBeVisible();
  }

  await enableTouring(page);
  await expect(reiter.getByRole('link', { name: 'Touring' })).toBeVisible();

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('eine Tournee mit Gastspiel entsteht und erscheint im Kalender', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await enableTouring(page);

  const stamp = Date.now();
  const venueName = `Kampnagel ${stamp}`;
  await venue(page, venueName);

  await page.goto('/touring');
  await page.getByRole('button', { name: 'Tournee anlegen' }).first().click();
  await page.getByLabel('Titel').fill(`Herbsttournee ${stamp}`);
  await page.getByRole('button', { name: 'Person hinzufügen' }).click();
  await page.getByRole('button', { name: 'Speichern' }).click();

  await expect(page.getByText(`Herbsttournee ${stamp}`)).toBeVisible();
  await expect(page.getByText('1 Reisende')).toBeVisible();

  // Das Gastspiel: Spielstätte, Reise, eine Vorstellung, Gage.
  await page.getByRole('button', { name: 'Gastspiel anlegen' }).first().click();
  await page.getByLabel('Spielstätte').selectOption({ label: `${venueName}, Hamburg` });
  await page.getByLabel('Stand').selectOption({ label: 'Bestätigt' });
  // In naher Zukunft, damit die Jahresansicht des Kalenders den Abend zeigt.
  await page.getByLabel('Anreise').fill(inDays(20));
  await page.getByLabel('Abreise').fill(inDays(22));
  await page.getByRole('button', { name: 'Vorstellung hinzufügen' }).click();
  await page.getByLabel('Tag der Vorstellung').fill(inDays(21));
  await page.getByLabel('Uhrzeit der Vorstellung').fill('19:30');
  await page.getByLabel('Gage').fill('4200');
  await page.getByRole('button', { name: 'Speichern' }).click();

  const zeile = page.getByRole('row').filter({ hasText: venueName });
  await expect(zeile).toBeVisible();
  await expect(zeile).toContainText('Bestätigt');
  await expect(zeile).toContainText('4.200');
  await expect(zeile).toContainText('noch nicht in Rechnung');

  // Im Kalender steht die Vorstellung -- gelesen, nicht kopiert. Als
  // Gastspiel gekennzeichnet, mit Verweis ins Touring statt eines Bogens.
  await page.goto('/kalender');
  await expect(page.getByText(`Herbsttournee ${stamp} · ${venueName}`).first()).toBeVisible();

  const range = await page.request.get(`/api/calendar/range?from=${inDays(19)}&to=${inDays(23)}`);
  const entries = (await range.json()).occurrences as { source: number; title: string; participants: unknown[] }[];
  const gastspiel = entries.filter((o) => o.title.includes(`Herbsttournee ${stamp}`));
  expect(gastspiel.map((o) => o.source)).toEqual([5, 5]);
  expect(gastspiel.every((o) => o.participants.length === 1)).toBeTruthy();

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('ein Kontakt ohne die Rolle Spielstätte wird als Ort abgewiesen', async ({ page }) => {
  await signIn(page);
  await enableTouring(page);

  // Über die Schnittstelle: Die Oberfläche bietet nur Spielstätten an, der
  // Server muss den Umweg trotzdem abfangen.
  const druckerei = await page.request.post('/api/contacts', {
    data: { kind: 1, name: `Druckerei ${Date.now()}`, roles: [1] },
  });
  const druckereiId = (await druckerei.json()).id as string;

  const tour = await page.request.post('/api/touring/tours', { data: { title: 'Probe-Tournee' } });
  const tourId = (await tour.json()).id as string;

  const stop = await page.request.post(`/api/touring/tours/${tourId}/stops`, {
    data: { venueId: druckereiId },
  });
  expect(stop.status()).toBe(400);
  expect(await stop.text()).toContain('Rolle Spielstätte');
});

/** Löschen fragt nach -- eine Tournee nimmt ihre Gastspiele samt Kalendereinträgen mit. */
test('eine Tournee wird erst nach Rückfrage gelöscht', async ({ page }) => {
  await signIn(page);
  await enableTouring(page);
  const title = `Versehen ${Date.now()}`;
  const antwort = await page.request.post('/api/touring/tours', { data: { title } });
  expect(antwort.ok(), await antwort.text()).toBeTruthy();

  await page.goto('/touring');
  await page.getByRole('button', { name: `Tournee ${title} löschen` }).click();
  const frage = page.getByRole('dialog', { name: 'Tournee löschen?' });
  await expect(frage).toBeVisible();

  // Abbrechen lässt sie stehen.
  await frage.getByRole('button', { name: 'Abbrechen' }).click();
  await expect(frage).toHaveCount(0);
  await expect(page.getByText(title)).toBeVisible();

  await page.getByRole('button', { name: `Tournee ${title} löschen` }).click();
  await frage.getByRole('button', { name: 'Endgültig löschen' }).click();
  await expect(frage).toHaveCount(0);
  await expect(page.getByText(title)).toHaveCount(0);
});
