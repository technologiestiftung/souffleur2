import { expect, test } from '@playwright/test';

const ADMIN = { email: 'admin@localhost', password: 'Entwicklung1234' };

/**
 * Fehler aus der Browserkonsole einsammeln -- sie sind hier der eigentliche Befund.
 *
 * Ein 401 auf /api/account/me ist keiner: Ohne Anmeldung ist das die richtige
 * Antwort, und Chrome protokolliert jeden abgelehnten Aufruf als Konsolenfehler.
 * Gezählt werden nur Fehler der Anwendung selbst.
 */
function collectErrors(page: import('@playwright/test').Page): string[] {
  const expected = /Failed to load resource.*(401|403|404)/;
  const errors: string[] = [];

  page.on('console', (message) => {
    if (message.type() === 'error' && !expected.test(message.text())) {
      errors.push(message.text());
    }
  });
  page.on('pageerror', (error) => errors.push(error.message));
  return errors;
}

test('die Anmeldeseite erscheint, ohne Fehler in der Konsole', async ({ page }) => {
  const errors = collectErrors(page);

  await page.goto('/');

  // Ohne Anmeldung führt jeder Weg zur Anmeldeseite.
  await expect(page).toHaveURL(/anmelden/);
  await expect(page.getByRole('heading', { name: 'souffleur' })).toBeVisible();
  await expect(page.getByLabel('E-Mail-Adresse')).toBeVisible();

  // Das ist der Kern: Eine weiße Seite bleibt sonst unbemerkt.
  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('nach der Anmeldung steht die Übersicht mit ihren Reitern', async ({ page }) => {
  const errors = collectErrors(page);

  await page.goto('/anmelden');
  await page.getByLabel('E-Mail-Adresse').fill(ADMIN.email);
  await page.getByLabel('Passwort').fill(ADMIN.password);
  await page.getByRole('button', { name: 'Anmelden' }).click();

  await expect(page).toHaveURL(/uebersicht/);

  const nav = page.getByRole('navigation', { name: 'Module' });
  await expect(nav.getByRole('link', { name: 'Übersicht' })).toBeVisible();
  await expect(nav.getByRole('link', { name: 'Finanzen' })).toBeVisible();
  await expect(nav.getByRole('link', { name: 'Kontakte' })).toBeVisible();

  // Dass ein abgeschaltetes Modul nicht auftaucht, prüft anlegen.spec.ts --
  // dort, wo es ein- und wieder ausgeschaltet wird. Hier stünde dieselbe
  // Zusage gegen denselben Server, und welcher Test zuerst drankommt,
  // entschiede über das Ergebnis.

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('das Journal zeigt die Buchungen', async ({ page }) => {
  await page.goto('/anmelden');
  await page.getByLabel('E-Mail-Adresse').fill(ADMIN.email);
  await page.getByLabel('Passwort').fill(ADMIN.password);
  await page.getByRole('button', { name: 'Anmelden' }).click();
  await expect(page).toHaveURL(/uebersicht/);

  await page.getByRole('navigation', { name: 'Module' }).getByRole('link', { name: 'Finanzen' }).click();

  await expect(page.getByRole('heading', { name: 'Journal' })).toBeVisible();
  await expect(page.getByText('Miete März, Hauptbühne')).toBeVisible();
  await expect(page.getByText('Entwurf').first()).toBeVisible();
});

test('hell und dunkel lassen sich umschalten', async ({ page }) => {
  await page.goto('/anmelden');
  await page.getByLabel('E-Mail-Adresse').fill(ADMIN.email);
  await page.getByLabel('Passwort').fill(ADMIN.password);
  await page.getByRole('button', { name: 'Anmelden' }).click();
  await expect(page).toHaveURL(/uebersicht/);

  const html = page.locator('html');
  const before = await html.getAttribute('data-theme');

  // Der Knopf trägt den aktuellen Zustand in seinem Namen.
  await page.getByRole('button', { name: /Erscheinungsbild/ }).click();
  await page.getByRole('button', { name: /Erscheinungsbild/ }).click();

  await expect(html).not.toHaveAttribute('data-theme', before ?? '');

  // Und es überlebt den Neuaufbau der Seite.
  const after = await html.getAttribute('data-theme');
  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('data-theme', after ?? '');
});

/** Anmelden -- die meisten Tests brauchen es. */
async function signIn(page: import('@playwright/test').Page): Promise<void> {
  await page.goto('/anmelden');
  await page.getByLabel('E-Mail-Adresse').fill(ADMIN.email);
  await page.getByLabel('Passwort').fill(ADMIN.password);
  await page.getByRole('button', { name: 'Anmelden' }).click();
  await expect(page).toHaveURL(/uebersicht/);
}

test('der Chat zeigt Kanäle und Gespräche an Gegenständen', async ({ page }) => {
  await signIn(page);

  await page.getByRole('button', { name: /Kommunikation aufklappen/ }).click();
  const chat = page.getByRole('complementary', { name: 'Kommunikation' });
  await expect(chat).toBeVisible();

  // Beide Arten: ein Kanal und ein Gespräch, das an einer Buchung hängt.
  await expect(chat.getByRole('button', { name: /Buchhaltung/ })).toBeVisible();
  await expect(chat.getByRole('button', { name: /Buchung/ })).toBeVisible();
});

test('eine Nachricht lässt sich schreiben und erscheint sofort', async ({ page }) => {
  await signIn(page);

  await page.getByRole('button', { name: /Kommunikation aufklappen/ }).click();
  const chat = page.getByRole('complementary', { name: 'Kommunikation' });
  await chat.getByRole('button', { name: /Buchhaltung/ }).click();

  // Die vorhandenen Nachrichten stehen da.
  await expect(chat.getByText('Die Stromrechnung hat zwei Zeiträume drin.')).toBeVisible();

  const text = `Probe ${Date.now()}`;
  await chat.getByLabel('Nachricht').fill(text);
  await chat.getByRole('button', { name: 'Senden' }).click();

  await expect(chat.getByText(text)).toBeVisible();

  // Nach dem Neuaufbau ist die Nachricht noch da -- also wirklich gespeichert.
  // Die Leiste bleibt dabei offen: Ihr Zustand wird gemerkt.
  await page.reload();
  const again = page.getByRole('complementary', { name: 'Kommunikation' });
  await expect(again).toBeVisible();

  await again.getByRole('button', { name: /Buchhaltung/ }).click();
  await expect(again.getByText(text)).toBeVisible();
});

test('der Chat hat keinen Reiter oben -- er lebt rechts', async ({ page }) => {
  await signIn(page);

  const nav = page.getByRole('navigation', { name: 'Module' });
  await expect(nav.getByRole('link', { name: 'Chat' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: /Kommunikation aufklappen/ })).toBeVisible();
});

test('die Seitenleisten klappen ein und aus', async ({ page }) => {
  await page.goto('/anmelden');
  await page.getByLabel('E-Mail-Adresse').fill(ADMIN.email);
  await page.getByLabel('Passwort').fill(ADMIN.password);
  await page.getByRole('button', { name: 'Anmelden' }).click();
  await expect(page).toHaveURL(/uebersicht/);

  await expect(page.getByRole('complementary', { name: 'Aktionen' })).toBeVisible();
  await page.getByRole('button', { name: 'Aktionsleiste zuklappen' }).click();
  await expect(page.getByRole('complementary', { name: 'Aktionen, zugeklappt' })).toBeVisible();

  await page.getByRole('button', { name: 'Kommunikation aufklappen' }).click();
  await expect(page.getByRole('complementary', { name: 'Kommunikation' })).toBeVisible();
});

test('die Kontakte zeigen Rollen und fehlende Einwilligung', async ({ page }) => {
  await page.goto('/anmelden');
  await page.getByLabel('E-Mail-Adresse').fill(ADMIN.email);
  await page.getByLabel('Passwort').fill(ADMIN.password);
  await page.getByRole('button', { name: 'Anmelden' }).click();
  await expect(page).toHaveURL(/uebersicht/);

  await page.getByRole('navigation', { name: 'Module' }).getByRole('link', { name: 'Kontakte' }).click();

  await expect(page.getByRole('heading', { name: 'Kontakte' })).toBeVisible();
  await expect(page.getByText('Druckerei Conrad')).toBeVisible();
  await expect(page.getByText('Behrens, Anja')).toBeVisible();

  // Nach Rolle filtern.
  await page.getByRole('button', { name: /^Förderer/ }).click();
  await expect(page.getByText('Senatsverwaltung für Kultur')).toBeVisible();
  await expect(page.getByText('Druckerei Conrad')).toHaveCount(0);
});

test('ein zugeklappter Streifen klappt beim Überfahren auf, auch angepinnt', async ({ page }) => {
  await signIn(page);

  // Rechts ist der Startzustand angepinnt und zugeklappt. Früher blieb der
  // Streifen dann stumm, während links das Überfahren schon aufklappte.
  const streifen = page.getByRole('complementary', { name: 'Kommunikation, zugeklappt' });
  const chat = page.getByRole('complementary', { name: 'Kommunikation', exact: true });
  await expect(streifen).toBeVisible();
  await expect(chat).toHaveCount(0);

  await streifen.hover();
  await expect(chat).toBeVisible();

  // Wer wieder geht, lässt sie zugehen -- nur hereingeschaut, nicht aufgeklappt.
  await page.getByRole('heading', { level: 1 }).hover();
  await expect(chat).toHaveCount(0);
  await expect(streifen).toBeVisible();
});
