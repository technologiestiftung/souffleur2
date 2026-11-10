import { expect, test, type Page } from '@playwright/test';

const ADMIN = { email: 'admin@localhost', password: 'Entwicklung1234' };

/**
 * Das Modul Personal zeigt die Dienste der nächsten vier Wochen -- wer an
 * einem Termin eingeteilt ist, und wo gegen den Bedarf der Terminart noch
 * jemand fehlt. Nicht die Besetzung: Die sagt, wer welche Rolle spielt, und
 * hängt an der Produktion, die es noch nicht gibt.
 */
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

test('Personal zeigt die Dienste und seine drei Ansichten', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);

  await page.getByRole('navigation', { name: 'Module' })
            .getByRole('link', { name: 'Personal' }).click();
  await expect(page).toHaveURL(/personal/);

  // Eingeteilt wird am Termin: Name, Funktion und Zusage stehen beieinander.
  await expect(page.getByText('Probe Hamlet').first()).toBeVisible();
  await expect(page.getByText('(Regie)').first()).toBeVisible();

  // Drei Fragen an dieselben Leute, drei Ansichten.
  await expect(page.getByRole('button', { name: 'Dienste' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Zeiten' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Künstlersozialabgabe' })).toBeVisible();

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('Fristen aus den Aufgaben zählen nicht als Dienst', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);

  await page.goto('/personal');

  // Eine Frist steht im Kalender, ist aber kein Dienst -- sie trägt
  // „Zuständig" als Funktion und würde die Zählung verfälschen.
  await expect(page.getByText('(Zuständig)')).toHaveCount(0);

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});
