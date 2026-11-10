import { expect, test, type Page } from '@playwright/test';

const ADMIN = { email: 'admin@localhost', password: 'Entwicklung1234' };
const KOLLEGE = { email: 'kollege@localhost', password: 'Entwicklung1234' };

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

/** Einen Kanal anlegen; danach steht das Teilnehmerfenster offen. */
async function kanalAnlegen(page: Page, name: string, geschlossen = false) {
  const aside = await leiste(page);
  await aside.getByRole('button', { name: 'Kanal anlegen' }).click();
  await aside.getByLabel('Name des Kanals').fill(name);
  if (geschlossen) {
    await aside.getByLabel(/Geschlossen/).check();
  }
  await aside.getByRole('button', { name: 'Anlegen', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Teilnehmer' })).toBeVisible();
  return aside;
}

/**
 * Das Beste aus zwei Welten: Wie in Slack bringt ein @ eine Vorschlagsliste
 * mit Personen und Gruppen, Enter übernimmt den Namen statt zu senden, und
 * die Erwähnung tritt in der Nachricht hervor. Ben erfährt davon an der
 * Glocke, ohne die Unterhaltung offen zu haben.
 */
test('ein @ bringt die Vorschlagsliste, und die Erwähnung landet bei Ben', async ({
  page,
  browser,
}) => {
  await signIn(page);
  const name = `Probe ${Date.now()}`;
  const aside = await kanalAnlegen(page, name);
  await page
    .getByRole('dialog', { name: 'Teilnehmer' })
    .getByRole('button', { name: 'Fertig' })
    .click();

  const ben = await browser.newContext({ baseURL: test.info().project.use.baseURL });
  const api = ben.request;
  expect(
    (await api.post('/api/account/login', { data: { ...KOLLEGE, rememberMe: false } })).ok(),
  ).toBeTruthy();

  try {
    const feld = aside.getByLabel('Nachricht');
    await feld.fill('Hallo @Be');
    const liste = aside.getByRole('listbox', { name: 'Erwähnen' });
    await expect(liste.getByRole('option', { name: 'Ben Schröder' })).toBeVisible();

    // Enter wählt, solange die Liste offen ist -- und sendet nicht.
    await feld.press('Enter');
    await expect(liste).toHaveCount(0);
    await expect(feld).toHaveValue('Hallo @Ben Schröder ');

    await feld.type('kommst du?');
    await feld.press('Enter');

    const nachricht = aside.getByText('kommst du?');
    await expect(nachricht).toBeVisible();
    await expect(nachricht.locator('span.text-brand')).toHaveText('@Ben Schröder');
    await expect(feld).toHaveValue('');

    // Bei Ben klingelt es.
    const glocke = (await (await api.get('/api/notifications')).json()) as {
      items: { title: string }[];
    };
    expect(glocke.items.some((n) => n.title.includes(`hat dich in #${name} erwähnt`))).toBeTruthy();
  } finally {
    await ben.close();
  }
});

/**
 * Ein Gruppenchat entsteht, indem man Personen oder eine ganze Gruppe
 * aufnimmt. Ein geschlossener Kanal ist für Unbeteiligte unsichtbar -- und
 * wird es wieder, sobald sie herausgenommen sind.
 */
test('Teilnehmer kommen einzeln oder als Gruppe dazu und gehen wieder', async ({
  page,
  browser,
}) => {
  await signIn(page);
  const ben = await browser.newContext({ baseURL: test.info().project.use.baseURL });
  const api = ben.request;
  expect(
    (await api.post('/api/account/login', { data: { ...KOLLEGE, rememberMe: false } })).ok(),
  ).toBeTruthy();

  const gruppenName = `Lichtprobe ${Date.now()}`;
  const konten = (await (await page.request.get('/api/access/users')).json()) as {
    id: string;
    name: string;
  }[];
  const benKonto = konten.find((k) => k.name.includes('Ben'))!;
  const gruppe = (await (
    await page.request.post('/api/access/groups', {
      data: { name: gruppenName, memberIds: [benKonto.id] },
    })
  ).json()) as { id: string };

  const name = `Leitungsrunde ${Date.now()}`;
  const sichtbarFuerBen = async () => {
    const liste = (await (await api.get('/api/chat/conversations')).json()) as {
      name: string | null;
    }[];
    return liste.some((c) => c.name === name);
  };

  try {
    await kanalAnlegen(page, name, true);
    const fenster = page.getByRole('dialog', { name: 'Teilnehmer' });
    const dabei = fenster.getByRole('list', { name: 'Dabei' });
    await expect(dabei.getByRole('listitem').filter({ hasText: '(ich)' })).toBeVisible();
    expect(await sichtbarFuerBen()).toBeFalsy();

    // Die ganze Gruppe aufnehmen: Ben ist drin.
    await fenster.getByLabel('Aufnehmen').fill('Lichtprobe');
    await fenster.getByRole('button', { name: new RegExp(`^${gruppenName}`) }).click();
    await expect(dabei.getByRole('listitem').filter({ hasText: 'Ben Schröder' })).toBeVisible();
    expect(await sichtbarFuerBen()).toBeTruthy();

    // Und wieder heraus.
    await fenster.getByRole('button', { name: 'Ben Schröder herausnehmen' }).click();
    await expect(dabei.getByRole('listitem').filter({ hasText: 'Ben Schröder' })).toHaveCount(0);
    expect(await sichtbarFuerBen()).toBeFalsy();

    // Einzeln geht es auch.
    await fenster.getByLabel('Aufnehmen').fill('Ben');
    await fenster.getByRole('button', { name: 'Ben Schröder' }).click();
    await expect(dabei.getByRole('listitem').filter({ hasText: 'Ben Schröder' })).toBeVisible();
    await fenster.getByRole('button', { name: 'Fertig' }).click();

    // Der Untertitel zählt mit.
    await expect(
      page
        .getByRole('complementary', { name: 'Kommunikation' })
        .getByText('geschlossen · 2 Personen'),
    ).toBeVisible();
  } finally {
    await page.request.delete(`/api/access/groups/${gruppe.id}`);
    await ben.close();
  }
});

/**
 * Archivieren ist der Regelweg: Der Kanal bleibt lesbar, nimmt aber nichts
 * mehr auf und wandert ins Archiv am Ende der Liste. Löschen gibt es nur für
 * Kanäle und nur nach Rückfrage.
 */
test('ein Kanal wird archiviert, wiederhergestellt und zuletzt gelöscht', async ({ page }) => {
  await signIn(page);
  const name = `Spielzeit ${Date.now()}`;
  const aside = await kanalAnlegen(page, name);
  await page
    .getByRole('dialog', { name: 'Teilnehmer' })
    .getByRole('button', { name: 'Fertig' })
    .click();

  const menue = aside.getByRole('button', { name: 'Unterhaltung verwalten' });
  await menue.click();
  await aside.getByRole('menuitem', { name: 'Archivieren' }).click();

  await expect(aside.getByText('Archiviert. Nimmt nichts Neues mehr auf.')).toBeVisible();
  await expect(aside.getByLabel('Nachricht')).toHaveCount(0);

  // Zurück in der Liste steht der Kanal im Archiv, nicht mehr oben.
  await aside.getByRole('button', { name: new RegExp(`^${name}`) }).click();
  await expect(aside.getByRole('button', { name: new RegExp(`^${name}`) })).toHaveCount(0);
  await aside.getByRole('button', { name: /^Archiv \(/ }).click();
  await aside.getByRole('button', { name: new RegExp(`^${name}`) }).click();

  await aside.getByRole('button', { name: 'Wiederherstellen' }).first().click();
  await expect(aside.getByLabel('Nachricht')).toBeVisible();

  // Löschen fragt nach -- und dann ist der Kanal weg.
  await menue.click();
  await aside.getByRole('menuitem', { name: 'Löschen …' }).click();
  const frage = page.getByRole('dialog', { name: 'Kanal löschen?' });
  await expect(frage).toBeVisible();
  await frage.getByRole('button', { name: 'Löschen' }).click();

  await expect(frage).toHaveCount(0);
  await expect(aside.getByText(name)).toHaveCount(0);
});

/** Eine Direktnachricht trägt den Namen der anderen Person, nicht „Direktnachricht". */
test('eine Direktnachricht heißt wie die Person', async ({ page }) => {
  await signIn(page);
  const aside = await leiste(page);

  await aside.getByRole('button', { name: 'Direktnachricht schreiben' }).click();
  const fenster = page.getByRole('dialog', { name: 'Direktnachricht an' });
  await fenster.getByLabel('Person suchen').fill('Ben');
  await fenster.getByRole('button', { name: 'Ben Schröder' }).click();

  await expect(fenster).toHaveCount(0);
  await expect(aside.getByText('Ben Schröder', { exact: true })).toBeVisible();
  await expect(aside.getByText('Direktnachricht', { exact: true })).toBeVisible();

  // Aus einer Direktnachricht nimmt man niemanden heraus, und löschen lässt sie sich nicht.
  await aside.getByRole('button', { name: 'Unterhaltung verwalten' }).click();
  await expect(aside.getByRole('menuitem', { name: 'Löschen …' })).toHaveCount(0);
  await expect(aside.getByRole('menuitem', { name: 'Archivieren' })).toBeVisible();
});
