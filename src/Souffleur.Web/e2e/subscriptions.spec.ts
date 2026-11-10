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

/** Legt ein Abonnement an und gibt dessen Adresse zurück. */
async function subscribe(page: Page, name: string): Promise<string> {
  await page.goto('/einstellungen/kalender-abos');
  await page.getByRole('button', { name: 'Abonnement anlegen' }).first().click();
  await page.getByLabel('Name', { exact: true }).fill(name);
  await page.getByRole('button', { name: 'Anlegen', exact: true }).click();

  const address = page.getByLabel(`Adresse von ${name}`);
  await expect(address).toBeVisible();
  return (await address.inputValue());
}

test('eine Adresse zum Abonnieren lässt sich anlegen', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);

  const name = `Spielplan ${Date.now()}`;
  const address = await subscribe(page, name);

  expect(address).toContain('/api/calendar/feed/');
  expect(address).toMatch(/\.ics$/);

  // Dass die Adresse selbst der Schlüssel ist, muss dastehen -- wer sie
  // weitergibt, gibt Zugang.
  await expect(page.getByText('Die Adresse ist der Schlüssel.')).toBeVisible();

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('die Adresse liefert einen Kalender, den fremde Software lesen kann', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);

  const address = await subscribe(page, `Feedprobe ${Date.now()}`);

  // Ohne Sitzung, so wie eine Kalenderanwendung fragt.
  const feed = await page.request.get(address, { headers: { cookie: '' } });

  expect(feed.status()).toBe(200);
  expect(feed.headers()['content-type']).toContain('text/calendar');

  const body = await feed.text();
  expect(body.startsWith('BEGIN:VCALENDAR')).toBe(true);
  expect(body).toContain('BEGIN:VTIMEZONE');

  // Die Serien gehen als Regel hinaus, nicht als ausgerechnete Termine.
  expect(body).toContain('RRULE:FREQ=WEEKLY');
  expect(body).toContain('SUMMARY:Probe Hamlet');

  // Der Beginn einer Serie muss ihr erster wirklicher Termin sein. Die
  // Gesellschafterversammlung ist der dritte Freitag im Monat -- angelegt
  // wurde sie an einem beliebigen Tag. Stünde der in DTSTART, zeigte jede
  // Kalenderanwendung eine Sitzung, die es nicht gibt: RFC 5545 zählt
  // DTSTART immer als Vorkommen mit.
  const board = body.split('BEGIN:VEVENT')
                    .find((part) => part.includes('SUMMARY:Gesellschafterversammlung'));
  expect(board).toContain('RRULE:FREQ=MONTHLY;BYDAY=3FR');

  const start = /DTSTART;TZID=[^:]+:(\d{4})(\d{2})(\d{2})T/.exec(board ?? '');
  expect(start).not.toBeNull();

  const [, year, month, day] = start!;
  expect(new Date(`${year}-${month}-${day}T12:00:00Z`).getUTCDay()).toBe(5);

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('eine neue Adresse macht die alte ungültig', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);

  const name = `Zurückziehen ${Date.now()}`;
  const before = await subscribe(page, name);

  await page.getByRole('listitem').filter({ hasText: name })
            .getByRole('button', { name: 'Neue Adresse' }).click();

  // Auf die neue Adresse warten statt sie sofort zu lesen -- sonst steht im
  // Feld noch die alte, und der Test prüft den Zustand von vorhin.
  const field = page.getByLabel(`Adresse von ${name}`);
  await expect(field).not.toHaveValue(before);

  const after = await field.inputValue();

  // Die alte antwortet nicht mehr, die neue schon.
  expect((await page.request.get(before, { headers: { cookie: '' } })).status()).toBe(404);
  expect((await page.request.get(after, { headers: { cookie: '' } })).status()).toBe(200);

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});
