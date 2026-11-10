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

/** Die Kommunikationsleiste aufklappen und zurückgeben. */
async function leiste(page: Page) {
  await page.getByRole('button', { name: /Kommunikation aufklappen/ }).click();
  const leiste = page.getByRole('complementary', { name: 'Kommunikation' });
  await expect(leiste).toBeVisible();
  return leiste;
}

/**
 * Die Post des Hauses unter dem Chat. Die Vorführdaten bringen zwei
 * angekommene Mails mit; gelesen wird hier nur die, die schon gelesen ist --
 * die Tests teilen sich eine Datenbank, und eine ungelesene soll ungelesen
 * bleiben.
 */
test('empfangene Mails stehen unter dem Chat und lassen sich lesen und beantworten', async ({
  page,
}) => {
  const errors = collectErrors(page);
  await signIn(page);
  const kommunikation = await leiste(page);

  const post = kommunikation.getByRole('list', { name: 'Post' });
  await expect(post.getByRole('button', { name: /Jens Marquardt/ })).toBeVisible();

  await post.getByRole('button', { name: /Bühnenbedarf Schmidt/ }).click();
  await expect(kommunikation.getByText('anbei unsere Rechnung 2026-1187')).toBeVisible();

  // Antworten: der Absender steht schon drin, der Betreff bekommt sein „Re:".
  await kommunikation.getByRole('button', { name: 'Antworten' }).click();
  await expect(kommunikation.getByText('Bühnenbedarf Schmidt GmbH', { exact: true })).toBeVisible();
  await expect(kommunikation.getByLabel('Betreff')).toHaveValue(/^Re: Rechnung 2026-1187/);

  await kommunikation.getByRole('button', { name: 'Zurück zur Post' }).click();
  await expect(post).toBeVisible();

  expect(errors).toEqual([]);
});

test('eine Mail an einen Kontakt lässt sich schreiben und steht danach in der Post', async ({
  page,
}) => {
  const errors = collectErrors(page);
  await signIn(page);
  const kommunikation = await leiste(page);

  await kommunikation.getByRole('button', { name: 'E-Mail schreiben' }).click();

  // Der Kontakt kommt aus dem Adressbuch: tippen, Vorschlag, Enter.
  const an = kommunikation.getByLabel('An', { exact: true });
  await an.fill('schmidt');
  await expect(kommunikation.getByRole('option')).toHaveCount(1);
  await an.press('Enter');
  await expect(kommunikation.getByText('Bühnenbedarf Schmidt GmbH', { exact: true })).toBeVisible();

  const betreff = `Probe ${Date.now()}`;
  await kommunikation.getByLabel('Betreff').fill(betreff);
  await kommunikation.getByLabel('Text').fill('Bitte liefern Sie bis Freitag.');
  await kommunikation.getByRole('button', { name: 'Senden' }).click();

  const post = kommunikation.getByRole('list', { name: 'Post' });
  await expect(post.getByRole('button', { name: new RegExp(betreff) })).toBeVisible();

  // Nach dem Neuaufbau noch da -- also wirklich abgelegt.
  await page.reload();
  const wieder = page.getByRole('complementary', { name: 'Kommunikation' });
  await expect(wieder.getByRole('button', { name: new RegExp(betreff) })).toBeVisible();

  expect(errors).toEqual([]);
});
