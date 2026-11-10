import { expect, test, type Page } from '@playwright/test';

const ADMIN = { email: 'admin@localhost', password: 'Entwicklung1234' };

/**
 * Urlaub beantragen, entscheiden, zurückziehen; krank melden.
 *
 * Das Konto der Verwaltung ist in den Vorführdaten mit „Krause, Anna"
 * verbunden und darf entscheiden -- als einzige im Haus auch über den
 * eigenen Antrag. Die Tests legen ihre Einträge in ein fernes Jahr und räumen
 * sie wieder weg: Sie laufen parallel gegen dieselbe Datenbank, und die
 * Vorführdaten sollen bleiben, wie sie sind.
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

async function openAbsences(page: Page): Promise<void> {
  // Über die Adresse, die auch die Glocke benutzt.
  await page.goto('/personal?ansicht=abwesenheiten');
  await expect(page.getByRole('heading', { name: 'Meine Abwesenheiten' })).toBeVisible();
}

test('Urlaub beantragen, genehmigen und wieder aufheben', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await openAbsences(page);

  await page.getByRole('button', { name: 'Urlaub beantragen' }).click();
  const bogen = page.getByRole('dialog', { name: 'Abwesenheit beantragen' });
  await bogen.getByLabel('von').fill('2031-07-07');
  await bogen.getByLabel('bis').fill('2031-07-11');
  await bogen.getByLabel('Notiz').fill('E2E-Urlaub');
  await bogen.getByRole('button', { name: 'Beantragen' }).click();
  await expect(bogen).toBeHidden();

  // Der Antrag wartet -- und steht bei denen, die entscheiden.
  const offen = page.getByRole('list', { name: 'Offene Anträge' }).getByRole('listitem').filter({
    hasText: 'E2E-Urlaub',
  });
  await expect(offen).toContainText('5 Tage');
  await offen.getByRole('button', { name: 'Genehmigen' }).click();
  await expect(offen).toHaveCount(0);

  // Im eigenen Jahr steht er nicht, er liegt in 2031 -- dorthin blättern.
  for (let i = new Date().getFullYear(); i < 2031; i++) {
    await page.getByRole('button', { name: 'Jahr vor' }).first().click();
  }

  const meiner = page.getByRole('list', { name: 'Meine Abwesenheiten' }).getByRole('listitem').filter({
    hasText: 'E2E-Urlaub',
  });
  await expect(meiner).toContainText('Genehmigt');

  // Was schon gilt, geht nur mit Grund weg.
  await meiner.getByRole('button', { name: 'zurückziehen' }).click();
  const aufheben = page.getByRole('dialog', { name: 'Aufheben' });
  await aufheben.getByRole('button', { name: 'Aufheben' }).click();
  await expect(aufheben.getByText('nur mit Grund')).toBeVisible();
  await aufheben.getByLabel('Grund').fill('Test räumt auf');
  await aufheben.getByRole('button', { name: 'Aufheben' }).click();
  await expect(aufheben).toBeHidden();
  await expect(meiner).toContainText('Zurückgezogen');

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('eine Krankmeldung hat kein Feld für die Diagnose', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await openAbsences(page);

  await page.getByRole('button', { name: 'Krank melden' }).click();
  const bogen = page.getByRole('dialog', { name: 'Krank melden' });
  await expect(bogen.getByText('Bitte keine Diagnose')).toBeVisible();
  await expect(bogen.getByLabel('Notiz')).toHaveCount(0);

  await bogen.getByLabel('seit').fill('2032-02-02');
  await bogen.getByLabel('voraussichtlich bis').fill('2032-02-03');
  await bogen.getByRole('button', { name: 'Melden' }).click();
  await expect(bogen).toBeHidden();

  // Gemeldet heißt: gilt sofort, und die Lohnbuchhaltung muss davon hören.
  const lohn = page.getByRole('list', { name: 'Noch nicht gemeldet' }).getByRole('listitem').filter({
    hasText: '02.02.–03.02.2032',
  });
  await expect(lohn).toBeVisible();
  await lohn.getByRole('button', { name: 'als gemeldet markieren' }).click();
  await expect(lohn).toHaveCount(0);

  // Aufräumen: im Jahr 2032 zurückziehen.
  for (let i = new Date().getFullYear(); i < 2032; i++) {
    await page.getByRole('button', { name: 'Jahr vor' }).first().click();
  }
  const meine = page.getByRole('list', { name: 'Meine Abwesenheiten' }).getByRole('listitem').filter({
    hasText: '02.02.–03.02.2032',
  });
  await meine.getByRole('button', { name: 'zurückziehen' }).click();
  const aufheben = page.getByRole('dialog', { name: 'Aufheben' });
  await aufheben.getByLabel('Grund').fill('Test räumt auf');
  await aufheben.getByRole('button', { name: 'Aufheben' }).click();
  await expect(meine).toContainText('Zurückgezogen');

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('die freien Tage kennen das Bundesland', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await page.goto('/einstellungen/freie-tage');

  await expect(page.getByLabel('Bundesland')).toHaveValue(/.+/);
  const jahr = page.getByRole('list', { name: 'Freie Tage im Jahr' });
  await expect(jahr).toContainText('Internationaler Frauentag');
  await expect(jahr).toContainText('Heiligabend');

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});
