import { expect, test, type Page } from '@playwright/test';

const ADMIN = { email: 'admin@localhost', password: 'Entwicklung1234' };

async function signIn(page: Page, email: string, password: string): Promise<void> {
  await page.goto('/anmelden');
  await page.getByLabel('E-Mail-Adresse').fill(email);
  await page.getByLabel('Passwort').fill(password);
  await page.getByRole('button', { name: 'Anmelden' }).click();
  await expect(page).toHaveURL(/uebersicht/);
}

test('ein Konto wird angelegt, die Person setzt ihr Passwort selbst und meldet sich an', async ({
  page,
  browser,
}) => {
  await signIn(page, ADMIN.email, ADMIN.password);
  const email = `tilda-${Date.now()}@example.invalid`;

  await page.goto('/einstellungen/zugaenge');
  await page.getByRole('button', { name: 'Konto', exact: true }).click();

  const bogen = page.getByRole('dialog', { name: 'Konto anlegen' });
  await bogen.getByLabel('Vorname').fill('Tilda');
  await bogen.getByLabel('Nachname').fill('Probe');
  await bogen.getByLabel('E-Mail-Adresse').fill(email);
  await bogen.getByRole('button', { name: 'Anlegen und einladen' }).click();

  // Ohne Mailserver steht der Link zum Weitergeben da.
  const einladung = page.getByRole('dialog', { name: 'Einladung für Tilda Probe' });
  const link = await einladung.getByLabel('Einladungslink').inputValue();
  expect(link).toContain('/einladung?user=');
  await einladung.getByRole('button', { name: 'Fertig' }).click();

  const zeile = page.getByRole('row').filter({ hasText: email });
  await expect(zeile).toContainText('eingeladen');

  // Die Eingeladene, in ihrem eigenen Browser.
  const fremd = await browser.newContext();
  const tilda = await fremd.newPage();
  await tilda.goto(link);
  await expect(tilda.getByText('Hallo Tilda Probe')).toBeVisible();
  await tilda.getByLabel('Neues Passwort').fill('Probebühne2027abc');
  await tilda.getByLabel('Noch einmal').fill('Probebühne2027abc');
  await tilda.getByRole('button', { name: 'Passwort festlegen' }).click();
  await expect(tilda.getByText('Das Passwort ist festgelegt')).toBeVisible();

  await signIn(tilda, email, 'Probebühne2027abc');

  // Ein zweites Mal geht derselbe Link nicht.
  await tilda.goto(link);
  await expect(tilda.getByText(/abgelaufen oder schon benutzt/)).toBeVisible();
  await fremd.close();

  // Gesperrt steht sie als gesperrt in der Liste.
  await page.reload();
  await page.getByRole('button', { name: 'Sperren: Tilda Probe' }).click();
  await expect(page.getByRole('row').filter({ hasText: email })).toContainText('gesperrt');
});
