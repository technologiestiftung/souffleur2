import { expect, test, type Page } from '@playwright/test';

const ADMIN = { email: 'admin@localhost', password: 'Entwicklung1234' };

async function signIn(page: Page): Promise<void> {
  await page.goto('/anmelden');
  await page.getByLabel('E-Mail-Adresse').fill(ADMIN.email);
  await page.getByLabel('Passwort').fill(ADMIN.password);
  await page.getByRole('button', { name: 'Anmelden' }).click();
  await expect(page).toHaveURL(/uebersicht/);
}

/**
 * Ein Tag weit genug in der Zukunft, dass die Vorführdaten nicht dazwischen
 * stehen, und je Test ein anderer, weil die Tests parallel laufen.
 */
function tagIn(tage: number): string {
  const d = new Date();
  d.setDate(d.getDate() + tage);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

function tagesfenster(page: Page) {
  // Das Tagesfenster ist das erste Fenster; der Bogen legt sich darüber.
  return page.getByRole('dialog').first();
}

test('ein Termin lässt sich bearbeiten und löschen', async ({ page }) => {
  await signIn(page);
  const tag = tagIn(61);
  const titel = `Bauprobe ${Date.now()}`;

  const angelegt = await page.request.post('/api/calendar/events', {
    data: { title: titel, startsAt: `${tag}T10:00:00+01:00`, endsAt: `${tag}T13:00:00+01:00` },
  });
  expect(angelegt.ok(), await angelegt.text()).toBeTruthy();

  await page.goto(`/kalender?tag=${tag}`);
  await tagesfenster(page).getByRole('button', { name: `${titel} bearbeiten` }).click();

  const bogen = page.getByRole('dialog', { name: 'Termin bearbeiten' });
  await expect(bogen.getByLabel('Titel')).toHaveValue(titel);
  await bogen.getByLabel('Titel').fill(`${titel} verschoben`);
  await bogen.getByLabel('Von').fill('14:00');
  await bogen.getByLabel('Bis').fill('17:00');
  await bogen.getByRole('button', { name: 'Speichern' }).click();
  await expect(bogen).toHaveCount(0);

  const zeile = tagesfenster(page).getByRole('listitem').filter({ hasText: `${titel} verschoben` });
  await expect(zeile).toContainText('14:00');

  // Löschen fragt einmal nach, ohne Browserdialog.
  await zeile.getByRole('button', { name: /bearbeiten/ }).click();
  await bogen.getByRole('button', { name: 'Löschen', exact: true }).click();
  await bogen.getByRole('button', { name: 'Wirklich löschen' }).click();
  await expect(bogen).toHaveCount(0);
  await expect(tagesfenster(page).getByText(`${titel} verschoben`)).toHaveCount(0);
});

test('aus einer Serie lässt sich nur ein Termin ändern', async ({ page }) => {
  await signIn(page);
  const tag = tagIn(68);
  const titel = `Wochenprobe ${Date.now()}`;

  const angelegt = await page.request.post('/api/calendar/series', {
    data: {
      title: titel,
      startDate: tag,
      startTime: '10:00:00',
      durationMinutes: 120,
      frequency: 2,
      interval: 1,
      byWeekday: 0,
      count: 3,
    },
  });
  expect(angelegt.ok(), await angelegt.text()).toBeTruthy();

  await page.goto(`/kalender?tag=${tag}`);
  await tagesfenster(page).getByRole('button', { name: `${titel} bearbeiten` }).click();

  // Die Vorgabe ist der eine Abend, nicht die ganze Serie.
  const bogen = page.getByRole('dialog', { name: 'Termin bearbeiten' });
  await expect(bogen.getByRole('radio', { name: 'Nur dieser Termin' })).toHaveAttribute(
    'aria-checked',
    'true',
  );
  await bogen.getByLabel('Titel').fill(`${titel} mit Gästen`);
  await bogen.getByRole('button', { name: 'Speichern' }).click();
  await expect(bogen).toHaveCount(0);
  await expect(tagesfenster(page).getByText(`${titel} mit Gästen`)).toBeVisible();

  // Eine Woche später steht die Serie wie vorher.
  await page.goto(`/kalender?tag=${tagIn(75)}`);
  await expect(tagesfenster(page).getByText(titel, { exact: true })).toBeVisible();
});

test('eine ganze Serie lässt sich ändern', async ({ page }) => {
  await signIn(page);
  const tag = tagIn(82);
  const titel = `Leseprobe ${Date.now()}`;

  const angelegt = await page.request.post('/api/calendar/series', {
    data: {
      title: titel,
      startDate: tag,
      startTime: '10:00:00',
      durationMinutes: 120,
      frequency: 2,
      interval: 1,
      byWeekday: 0,
      count: 3,
    },
  });
  expect(angelegt.ok(), await angelegt.text()).toBeTruthy();

  await page.goto(`/kalender?tag=${tag}`);
  await tagesfenster(page).getByRole('button', { name: `${titel} bearbeiten` }).click();

  await page.getByRole('radio', { name: 'Ganze Serie' }).click();
  const bogen = page.getByRole('dialog', { name: 'Serie bearbeiten' });
  await expect(bogen.getByText('Wiederholung')).toBeVisible();
  await bogen.getByLabel('Titel').fill(`${titel} am Tisch`);
  await bogen.getByRole('button', { name: 'Speichern' }).click();
  await expect(bogen).toHaveCount(0);

  await page.goto(`/kalender?tag=${tagIn(89)}`);
  await expect(tagesfenster(page).getByText(`${titel} am Tisch`)).toBeVisible();
});
