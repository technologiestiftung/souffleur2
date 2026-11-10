import { expect, test, type Page } from '@playwright/test';

const ADMIN = { email: 'admin@localhost', password: 'Entwicklung1234' };

async function signIn(page: Page): Promise<void> {
  await page.goto('/anmelden');
  await page.getByLabel('E-Mail-Adresse').fill(ADMIN.email);
  await page.getByLabel('Passwort').fill(ADMIN.password);
  await page.getByRole('button', { name: 'Anmelden' }).click();
  await expect(page).toHaveURL(/uebersicht/);
}

/** Ein eigener Kanal je Test -- die Tests laufen parallel gegen eine Datenbank. */
async function kanal(page: Page, name: string): Promise<void> {
  const angelegt = await page.request.post('/api/chat/conversations', { data: { name } });
  expect(angelegt.ok(), await angelegt.text()).toBeTruthy();
  await page.reload();
  await page.getByRole('button', { name: /Kommunikation aufklappen/ }).click();
  await page
    .getByRole('complementary', { name: 'Kommunikation', exact: true })
    .getByRole('button', { name: new RegExp(name) })
    .click();
}

test('auf eine Nachricht lässt sich antworten, mit Zitat', async ({ page }) => {
  await signIn(page);
  const name = `Faden ${Date.now()}`;
  await kanal(page, name);
  const chat = page.getByRole('complementary', { name: 'Kommunikation', exact: true });

  await chat.getByLabel('Nachricht', { exact: true }).fill('Wer holt die Scheinwerfer?');
  await chat.getByRole('button', { name: 'Senden' }).click();
  await expect(chat.getByText('Wer holt die Scheinwerfer?')).toBeVisible();

  await chat.getByText('Wer holt die Scheinwerfer?').hover();
  await chat.getByRole('button', { name: /^Antworten auf/ }).click();
  await expect(chat.getByText(/Antwort auf .*Wer holt die Scheinwerfer/)).toBeVisible();

  await chat.getByLabel('Nachricht', { exact: true }).fill('Mache ich am Donnerstag.');
  await chat.getByRole('button', { name: 'Senden' }).click();

  // Die Antwort trägt ihr Zitat -- und die Leiste darüber ist wieder leer.
  const zitat = chat.getByRole('button', { name: /Wer holt die Scheinwerfer\?/ });
  await expect(zitat).toBeVisible();
  await expect(chat.getByText('Mache ich am Donnerstag.')).toBeVisible();
  await expect(chat.getByText(/^Antwort auf/)).toHaveCount(0);
});

test('die eigene Nachricht lässt sich ändern und trägt „bearbeitet"', async ({ page }) => {
  await signIn(page);
  const name = `Ändern ${Date.now()}`;
  await kanal(page, name);
  const chat = page.getByRole('complementary', { name: 'Kommunikation', exact: true });

  await chat.getByLabel('Nachricht', { exact: true }).fill('Probe um 10 Uhr.');
  await chat.getByRole('button', { name: 'Senden' }).click();
  await chat.getByText('Probe um 10 Uhr.').hover();
  await chat.getByRole('button', { name: 'Eigene Nachricht ändern' }).click();

  await chat.getByLabel('Geänderter Text').fill('Probe um 11 Uhr.');
  await chat.getByRole('button', { name: 'Speichern' }).click();

  await expect(chat.getByText('Probe um 11 Uhr.')).toBeVisible();
  await expect(chat.getByText('Probe um 10 Uhr.')).toHaveCount(0);
  await expect(chat.getByText('bearbeitet')).toBeVisible();
});

test('ein Kanal für eine Gruppe nimmt ihre Mitglieder auf', async ({ page }) => {
  await signIn(page);
  const api = page.request;
  const ich = (await (await api.get('/api/account/me')).json()) as { id: string };
  const gruppenname = `Bühnentechnik ${Date.now()}`;
  const gruppe = (await (
    await api.post('/api/access/groups', { data: { name: gruppenname, memberIds: [ich.id] } })
  ).json()) as { id: string };

  try {
    await page.reload();
    await page.getByRole('button', { name: /Kommunikation aufklappen/ }).click();
    const chat = page.getByRole('complementary', { name: 'Kommunikation', exact: true });
    await chat.getByRole('button', { name: 'Kanal anlegen' }).click();
    const kanalname = `Technikkanal ${Date.now()}`;
    await chat.getByPlaceholder('Name des Kanals').fill(kanalname);
    await chat.getByLabel(/Für eine Gruppe/).selectOption({ label: gruppenname });
    await expect(chat.getByText('auch wer später dazukommt')).toBeVisible();
    await chat.getByRole('button', { name: 'Anlegen', exact: true }).click();

    // Ich bin in der Gruppe, also im Kanal -- und der Kanal sagt, woher seine Leute kommen.
    await expect(chat.getByRole('button', { name: `${kanalname} 1 Person` })).toBeVisible();
    await chat.getByRole('button', { name: 'Unterhaltung verwalten' }).click();
    await chat.getByRole('menuitem', { name: /Teilnehmer/ }).click();
    await expect(page.getByText(`folgt der Gruppe ${gruppenname}`)).toBeVisible();
    await expect(page.getByRole('button', { name: /herausnehmen$/ })).toHaveCount(0);
  } finally {
    await api.delete(`/api/access/groups/${gruppe.id}`);
  }
});
