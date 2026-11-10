import { expect, test, type Page } from '@playwright/test';

const ADMIN = { email: 'admin@localhost', password: 'Entwicklung1234' };
const KOLLEGE = { email: 'kollege@localhost', password: 'Entwicklung1234' };

async function signIn(page: Page): Promise<void> {
  await page.goto('/anmelden');
  await page.getByLabel('E-Mail-Adresse').fill(ADMIN.email);
  await page.getByLabel('Passwort').fill(ADMIN.password);
  await page.getByRole('button', { name: 'Anmelden' }).click();
  await expect(page).not.toHaveURL(/anmelden/);
}

/**
 * Die Glocke klingelt nur, wenn jemand anderes handelt -- wer selbst
 * handelt, bekommt keinen Hinweis. Darum zwei Personen: Ben in einem
 * eigenen Browser-Kontext, Anna (das Entwicklungskonto) im Fenster.
 */
test('wer eine Aufgabe bekommt, sieht es in der Glocke, ohne neu zu laden', async ({ page, browser }) => {
  await signIn(page);

  const ben = await browser.newContext({ baseURL: test.info().project.use.baseURL });
  const api = ben.request;
  expect((await api.post('/api/account/login', { data: { ...KOLLEGE, rememberMe: false } })).ok()).toBeTruthy();

  // Das Entwicklungskonto haengt am Kontakt „Krause, Anna".
  const kontakte = (await (await api.get('/api/contacts?take=500')).json()) as { id: string; name: string }[];
  const anna = kontakte.find((k) => k.name === 'Krause, Anna')!;

  const titel = `Requisite zählen ${Date.now()}`;
  const aufgabe = (await (
    await api.post('/api/tasks', { data: { title: titel, assigneeId: anna.id } })
  ).json()) as { id: string };

  try {
    // Ohne Neuladen: Der Zähler an der Glocke springt über die offene Verbindung.
    const glocke = page.getByRole('button', { name: /^Benachrichtigungen/ });
    await expect(glocke).toHaveAccessibleName(/ungelesen/);

    await glocke.click();
    const eintrag = page.getByRole('button', { name: new RegExp(`Ben Schröder hat dir eine Aufgabe gegeben: ${titel}`) });
    await expect(eintrag).toBeVisible();

    await eintrag.click();
    await expect(page).toHaveURL(/aufgaben/);
  } finally {
    await api.delete(`/api/tasks/${aufgabe.id}`);
    await ben.close();
  }
});

test('die E-Mail-Einstellung bleibt gespeichert', async ({ page }) => {
  await signIn(page);
  await page.goto('/einstellungen/benachrichtigungen');

  const auswahl = page.getByLabel('Mail bei Aufgaben an mich oder meine Gruppen');
  await auswahl.selectOption({ label: 'täglich gesammelt' });
  await expect(page.getByText('Gespeichert.')).toBeVisible();

  await page.reload();
  await expect(page.getByLabel('Mail bei Aufgaben an mich oder meine Gruppen')).toHaveValue(/2/);

  // Zurueck auf die Vorgabe -- andere Tests sollen ein ungestoertes Haus vorfinden.
  await page.getByLabel('Mail bei Aufgaben an mich oder meine Gruppen').selectOption({ label: 'keine Mail' });
  await expect(page.getByText('Gespeichert.')).toBeVisible();
});
