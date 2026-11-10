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
 * Was dieser Test verstellt, stellt er zurück: Der Kontenrahmen des Hauses
 * gilt für den ganzen Server, und mit SKR42 verlangte das Journal in den
 * anderen Tests auf einmal eine Sphäre.
 */
test.afterEach(async ({ page }) => {
  await page.request.put('/api/organization', {
    data: {
      name: 'Theater am Kanal',
      legalForm: 'GmbH',
      street: 'Kantstraße 12a',
      postalCode: '10623',
      city: 'Berlin',
      chartOfAccounts: 3,
      isNonProfit: false,
    },
  });
});

test('das Haus wählt Rechtsform und Kontenrahmen, und beides bleibt', async ({ page }) => {
  await signIn(page);

  // Über die Einstellungen, nicht über die Adresszeile: Der Bereich muss
  // in der Leiste stehen.
  await page.goto('/einstellungen/module');
  await page
    .getByRole('complementary', { name: 'Aktionen' })
    .getByRole('link', { name: 'Haus' })
    .click();
  await expect(page).toHaveURL(/einstellungen\/haus/);
  await expect(page.getByLabel('Name')).toHaveValue('Theater am Kanal');

  // Eine gGmbH ist gemeinnützig -- der Haken folgt der Rechtsform.
  const gemeinnuetzig = page.getByRole('checkbox', { name: /gemeinnützig anerkannt/ });
  await expect(gemeinnuetzig).not.toBeChecked();
  await page.getByRole('combobox', { name: 'Rechtsform', exact: true }).selectOption('gGmbH');
  await expect(gemeinnuetzig).toBeChecked();

  // Damit stehen die vier Sphären erklärt da.
  await expect(page.getByText('Zweckbetrieb')).toBeVisible();

  await page.getByRole('radio', { name: /SKR42/ }).check();
  await page.getByRole('button', { name: 'Speichern' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Gespeichert.' })).toBeVisible();

  await page.reload();
  await expect(page.getByRole('combobox', { name: 'Rechtsform', exact: true })).toHaveValue(
    'gGmbH',
  );
  await expect(page.getByRole('radio', { name: /SKR42/ })).toBeChecked();
  await expect(page.getByRole('checkbox', { name: /gemeinnützig anerkannt/ })).toBeChecked();
});

test('eine Rechtsform, die nicht in der Liste steht, lässt sich hinschreiben', async ({ page }) => {
  await signIn(page);
  await page.goto('/einstellungen/haus');

  await page.getByRole('combobox', { name: 'Rechtsform', exact: true }).selectOption('andere');
  const frei = page.getByLabel('Rechtsform, ausgeschrieben');
  await frei.fill('Anstalt des öffentlichen Rechts');
  await page.getByRole('button', { name: 'Speichern' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Gespeichert.' })).toBeVisible();

  await page.reload();
  await expect(page.getByRole('combobox', { name: 'Rechtsform', exact: true })).toHaveValue(
    'andere',
  );
  await expect(page.getByLabel('Rechtsform, ausgeschrieben')).toHaveValue(
    'Anstalt des öffentlichen Rechts',
  );
});
