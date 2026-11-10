import { expect, test, type Page } from '@playwright/test';

const ADMIN = { email: 'admin@localhost', password: 'Entwicklung1234' };

/**
 * Anlegen, von der Übersicht aus.
 *
 * Die Aktionsleiste links meldet ihre Knöpfe über eine Registry an, und ein
 * Knopf ohne `run` tut nichts -- stumm, ohne Fehler, ohne Hinweis. Genau das
 * war am 02.11.2026 auf Übersicht, Journal und Kontakten der Fall. Diese
 * Tests gehen deshalb über die Knöpfe und nicht über die Adresszeile.
 */
function collectErrors(page: Page): string[] {
  // 400 gehört hier dazu: Ein zurückgewiesener Bogen ist der Normalfall,
  // den diese Tests ausdrücklich auslösen.
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

test('die Schnellaktionen der Übersicht führen irgendwohin', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);

  const leiste = page.getByRole('complementary', { name: 'Aktionen' });

  await leiste.getByRole('button', { name: 'Beleg einreichen' }).click();
  await expect(page).toHaveURL(/finanzen\/belege/);
  await expect(page.getByLabel('Belegdatum')).toBeVisible();

  await page.goBack();
  await leiste.getByRole('button', { name: 'Aufgabe anlegen' }).click();
  await expect(page).toHaveURL(/aufgaben/);
  await expect(page.getByLabel('Titel')).toBeVisible();

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('ein Kontakt lässt sich anlegen und wieder ändern', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);

  await page
    .getByRole('navigation', { name: 'Module' })
    .getByRole('link', { name: 'Kontakte' })
    .click();

  await page
    .getByRole('complementary', { name: 'Aktionen' })
    .getByRole('button', { name: 'Kontakt anlegen' })
    .click();

  // Ohne Rolle geht es nicht weiter -- dieselbe Regel wie auf dem Server.
  await page.getByLabel('Name', { exact: true }).fill('Probebühne Wedding e. V.');
  await page.getByRole('button', { name: 'Speichern' }).click();
  await expect(page.getByText('mindestens eine Rolle')).toBeVisible();

  await page.getByRole('button', { name: 'Lieferant', exact: true }).click();
  await page.getByLabel('Ort', { exact: true }).fill('Berlin');
  await page.getByRole('button', { name: 'Speichern' }).click();

  const zeile = page.getByRole('row', { name: /Probebühne Wedding/ });
  await expect(zeile).toBeVisible();

  // Ein Klick auf die Zeile öffnet denselben Bogen mit dem, was dasteht.
  await zeile.click();
  await expect(page.getByLabel('Name', { exact: true })).toHaveValue('Probebühne Wedding e. V.');

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('eine Buchung entsteht als Entwurf und lässt sich festschreiben', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);

  await page
    .getByRole('navigation', { name: 'Module' })
    .getByRole('link', { name: 'Finanzen' })
    .click();

  await page
    .getByRole('complementary', { name: 'Aktionen' })
    .getByRole('button', { name: 'Buchung erfassen' })
    .click();

  await page.getByLabel('Buchungstext').fill('Materialkauf Werkstatt');

  const konten = page.getByLabel('Konto');
  await konten.nth(0).selectOption({ index: 1 });
  await konten.nth(1).selectOption({ index: 2 });

  // Eine Buchung, die nicht aufgeht, nimmt der Bogen gar nicht erst an.
  await page.getByLabel('Soll').nth(0).fill('119,00');
  await page.getByRole('button', { name: 'Als Entwurf anlegen' }).click();
  await expect(page.getByText('Soll und Haben stimmen nicht überein.')).toBeVisible();

  await page.getByLabel('Haben').nth(1).fill('119,00');
  await expect(page.getByText('geht auf')).toBeVisible();
  await page.getByRole('button', { name: 'Als Entwurf anlegen' }).click();

  const zeile = page.getByRole('row', { name: /Materialkauf Werkstatt/ });
  await expect(zeile).toBeVisible();
  await expect(zeile.getByText('Entwurf')).toBeVisible();

  // Festgeschrieben wird ausdrücklich -- danach ist sie unveränderlich.
  await zeile.getByRole('button', { name: 'festschreiben' }).click();
  await expect(
    page.getByRole('row', { name: /Materialkauf Werkstatt/ }).getByText('Entwurf'),
  ).toHaveCount(0);

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('ein Haus schaltet Ticketing an und richtet den Vorverkauf ein', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);

  // Ticketing ist in einer frischen Installation aus: Es braucht einen Zugang
  // zu einem Fremdsystem, den es noch nicht gibt.
  const reiter = page.getByRole('navigation', { name: 'Module' });
  await expect(reiter.getByRole('link', { name: 'Ticketing' })).toHaveCount(0);

  await page.goto('/einstellungen/module');
  const ticketing = page.getByRole('listitem').filter({ hasText: 'Ticketing' });
  const schalter = ticketing.getByRole('switch');
  await expect(schalter).not.toBeChecked();
  await schalter.click();

  await expect(reiter.getByRole('link', { name: 'Ticketing' })).toBeVisible();

  await reiter.getByRole('link', { name: 'Ticketing' }).click();
  await page.getByRole('button', { name: 'pretix', exact: true }).click();

  // Ohne Token kommt der Bogen nicht durch -- und sagt auch, woran es liegt.
  await page.getByLabel('Adresse der Instanz').fill('https://pretix.example.org');
  await page.getByLabel('Veranstalter').fill('probebuehne');
  await page.getByRole('button', { name: 'Speichern und prüfen' }).click();
  await expect(page.getByText('Ohne Token')).toBeVisible();

  // Wieder abschalten. Der Server merkt sich den Stand über den ganzen Lauf,
  // und andere Tests gehen von einer frischen Installation aus -- ohne das
  // hinge das Ergebnis davon ab, wer zuerst drankommt.
  await page.goto('/einstellungen/module');
  await expect(schalter).toBeChecked();
  await schalter.click();
  await expect(reiter.getByRole('link', { name: 'Ticketing' })).toHaveCount(0);

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});
