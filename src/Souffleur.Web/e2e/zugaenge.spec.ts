import { expect, test, type Page } from '@playwright/test';

const ADMIN = { email: 'admin@localhost', password: 'Entwicklung1234' };

async function signIn(page: Page): Promise<void> {
  await page.goto('/anmelden');
  await page.getByLabel('E-Mail-Adresse').fill(ADMIN.email);
  await page.getByLabel('Passwort').fill(ADMIN.password);
  await page.getByRole('button', { name: 'Anmelden' }).click();
  await expect(page).not.toHaveURL(/anmelden/);
}

/**
 * Die Verwaltung der Zugaenge. Raeumt hinter sich auf: Gruppen und Rollen
 * liegen in derselben Datenbank, und der naechste Test soll sie nicht
 * vorfinden.
 */
test('eine Gruppe entsteht mit Rolle und Mitglied und taucht bei der Person auf', async ({ page }) => {
  await signIn(page);
  await page.goto('/einstellungen/zugaenge');

  await page.getByRole('button', { name: 'Gruppe anlegen' }).click();
  const fenster = page.getByRole('dialog', { name: 'Gruppe anlegen' });
  await fenster.getByLabel('Name').fill('Probetechnik');
  await fenster.getByRole('checkbox', { name: 'Disposition' }).check();
  await fenster.getByRole('checkbox', { name: 'Entwicklung Admin' }).check();
  await fenster.getByRole('button', { name: 'Speichern' }).click();
  await expect(fenster).toHaveCount(0);

  const gruppe = page.getByRole('listitem').filter({ hasText: 'Probetechnik' });
  await expect(gruppe).toBeVisible();
  await expect(gruppe.getByText('Disposition')).toBeVisible();

  // Bei der Person steht die Gruppe -- daher kommen ihre Rechte.
  await page.getByRole('tab', { name: 'Personen' }).click();
  const zeile = page.getByRole('row').filter({ hasText: 'Entwicklung Admin' });
  await expect(zeile.getByText('Probetechnik')).toBeVisible();

  // Aufraeumen
  await page.getByRole('tab', { name: 'Gruppen' }).click();
  await gruppe.getByRole('button', { name: 'ändern' }).click();
  await page.getByRole('button', { name: 'Gruppe löschen' }).click();
  await expect(gruppe).toHaveCount(0);
});

test('eine mitgelieferte Rolle laesst sich aendern, aber nicht loeschen', async ({ page }) => {
  await signIn(page);
  await page.goto('/einstellungen/zugaenge');
  await page.getByRole('tab', { name: 'Rollen' }).click();

  const admin = page.getByRole('listitem').filter({ hasText: 'Administration' });
  await expect(admin.getByText('mitgeliefert')).toBeVisible();
  await admin.getByRole('button', { name: 'ändern' }).click();

  const fenster = page.getByRole('dialog', { name: 'Rolle ändern' });
  await expect(fenster.getByRole('checkbox', { name: 'Konten, Rollen und Gruppen verwalten' })).toBeChecked();
  await expect(fenster.getByRole('button', { name: 'Rolle löschen' })).toHaveCount(0);
  await fenster.getByRole('button', { name: 'Abbrechen' }).click();
});

test('eine eigene Rolle entsteht mit einem ganzen Bereich und laesst sich wieder loeschen', async ({ page }) => {
  await signIn(page);
  await page.goto('/einstellungen/zugaenge');

  await page.getByRole('button', { name: 'Rolle anlegen' }).click();
  const fenster = page.getByRole('dialog', { name: 'Rolle anlegen' });
  await fenster.getByLabel('Name').fill('Dramaturgie');
  // Der Bereich als Ganzes: ein Haken, alle Rechte darunter.
  await fenster.getByRole('checkbox', { name: 'Dokumente', exact: true }).check();
  await fenster.getByRole('button', { name: 'Speichern' }).click();

  const rolle = page.getByRole('listitem').filter({ hasText: 'Dramaturgie' });
  await expect(rolle.getByText(/3 von \d+ Rechten/)).toBeVisible();

  await rolle.getByRole('button', { name: 'ändern' }).click();
  await page.getByRole('button', { name: 'Rolle löschen' }).click();
  await expect(rolle).toHaveCount(0);
});

test('zweimal derselbe Gruppenname wird abgewiesen, mit Grund', async ({ page }) => {
  await signIn(page);
  await page.goto('/einstellungen/zugaenge');

  for (const versuch of [1, 2]) {
    await page.getByRole('button', { name: 'Gruppe anlegen' }).click();
    const fenster = page.getByRole('dialog', { name: 'Gruppe anlegen' });
    await fenster.getByLabel('Name').fill('Doppelt');
    await fenster.getByRole('button', { name: 'Speichern' }).click();
    if (versuch === 2) {
      await expect(fenster.getByText('Eine Gruppe mit diesem Namen gibt es schon.')).toBeVisible();
      await fenster.getByRole('button', { name: 'Abbrechen' }).click();
    }
  }

  const gruppe = page.getByRole('listitem').filter({ hasText: 'Doppelt' });
  await gruppe.getByRole('button', { name: 'ändern' }).click();
  await page.getByRole('button', { name: 'Gruppe löschen' }).click();
  await expect(gruppe).toHaveCount(0);
});

test('eine Aufgabe geht an eine Gruppe und taucht unter „Nur meine Gruppen" auf', async ({ page }) => {
  await signIn(page);

  // Eine Gruppe, in der ich selbst bin.
  await page.goto('/einstellungen/zugaenge');
  await page.getByRole('button', { name: 'Gruppe anlegen' }).click();
  const fenster = page.getByRole('dialog', { name: 'Gruppe anlegen' });
  await fenster.getByLabel('Name').fill('Bühnentechnik');
  await fenster.getByRole('checkbox', { name: 'Entwicklung Admin' }).check();
  await fenster.getByRole('button', { name: 'Speichern' }).click();
  await expect(fenster).toHaveCount(0);

  // Zwei Aufgaben: eine an die Gruppe, eine ohne.
  await page.goto('/aufgaben');
  const mitGruppe = `Leiter prüfen ${Date.now()}`;
  const ohneGruppe = `Kaffee kaufen ${Date.now()}`;

  await page.getByRole('button', { name: 'Aufgabe anlegen' }).first().click();
  await page.getByLabel('Titel').fill(mitGruppe);
  // Im Fenster suchen: Der Filter „Nur meine Gruppen" traegt dasselbe Wort.
  await page.getByRole('dialog').getByLabel('Gruppe').selectOption({ label: 'Bühnentechnik' });
  await page.getByRole('button', { name: 'Speichern' }).click();

  await page.getByRole('button', { name: 'Aufgabe anlegen' }).first().click();
  await page.getByLabel('Titel').fill(ohneGruppe);
  await page.getByRole('button', { name: 'Speichern' }).click();

  // Die Gruppe steht an der Aufgabe.
  const zeile = page.getByRole('listitem').filter({ hasText: mitGruppe }).last();
  await expect(zeile.getByText('Bühnentechnik')).toBeVisible();

  // Nur meine Gruppen: die eine ja, die andere nicht.
  await page.getByLabel('Nur meine Gruppen').check();
  await expect(page.getByText(mitGruppe)).toBeVisible();
  await expect(page.getByText(ohneGruppe)).toHaveCount(0);

  // Aufraeumen: beide Aufgaben erledigen, die Gruppe loeschen.
  await page.getByLabel('Nur meine Gruppen').uncheck();
  await page.getByRole('checkbox', { name: `${mitGruppe} erledigen` }).check();
  await page.getByRole('checkbox', { name: `${ohneGruppe} erledigen` }).check();

  await page.goto('/einstellungen/zugaenge');
  await page.getByRole('tab', { name: 'Gruppen' }).click();
  const gruppe = page.getByRole('listitem').filter({ hasText: 'Bühnentechnik' });
  await gruppe.getByRole('button', { name: 'ändern' }).click();
  await page.getByRole('button', { name: 'Gruppe löschen' }).click();
  await expect(gruppe).toHaveCount(0);
});

/**
 * Eine Stelle mit Gruppe schlaegt zuerst deren Leute vor.
 *
 * Mit eigener Terminart und eigenem Termin, beides ueber die Schnittstelle
 * angelegt: Die Vorstellungen der Vorfuehrdaten gehoeren dem Besetzen-Test
 * nebenan, und die Tests laufen gleichzeitig. Wer dort den Bedarf aendert,
 * nimmt dem anderen Test die Leute weg.
 */
test('beim Besetzen stehen zuerst die Leute der vorgeschlagenen Gruppe zur Wahl', async ({ page }) => {
  await signIn(page);
  const api = page.request;

  type Eintrag = { id: string; key: string; label: string };
  const kategorien = (await (await api.get('/api/catalogs/EventCategory')).json()) as Eintrag[];
  const generalprobe = kategorien.find((k) => k.key === 'generalprobe')!;
  const funktionen = (await (await api.get('/api/catalogs/ParticipationRole')).json()) as Eintrag[];
  const funktion = funktionen[0];
  const ich = (await (await api.get('/api/account/me')).json()) as { id: string };

  // Eine Gruppe, in der nur ich bin -- mein Konto haengt in den
  // Vorfuehrdaten am Kontakt „Krause, Anna".
  const gruppe = (await (
    await api.post('/api/access/groups', { data: { name: 'Lichtprobe', memberIds: [ich.id] } })
  ).json()) as { id: string };

  await api.put(`/api/personnel/duty-templates/${generalprobe.id}`, {
    data: { requirements: [{ functionId: funktion.id, count: 1, groupId: gruppe.id }], note: null },
  });

  const titel = `Generalprobe Gruppentest ${Date.now()}`;
  const morgen = new Date();
  morgen.setDate(morgen.getDate() + 1);
  morgen.setHours(18, 0, 0, 0);
  const ende = new Date(morgen.getTime() + 3 * 60 * 60 * 1000);
  const termin = (await (
    await api.post('/api/calendar/events', {
      data: {
        title: titel,
        startsAt: morgen.toISOString(),
        endsAt: ende.toISOString(),
        categoryId: generalprobe.id,
      },
    })
  ).json()) as { id: string };

  try {
    await page.goto('/personal');
    const dienst = page.getByRole('listitem').filter({ hasText: titel }).last();
    await dienst.getByRole('button', { name: `${funktion.label} besetzen` }).click();

    // textContent statt innerText: Die Eintraege einer geschlossenen
    // Auswahlliste werden nicht gezeichnet, innerText liefert dort nichts.
    // Und warten -- die Gruppe kommt erst nach dem Oeffnen des Fensters.
    const wer = page.getByRole('dialog').getByLabel('Wer');
    const eintraege = async () => (await wer.locator('option').allTextContents()).map((s) => s.trim());

    // Platzhalter plus genau eine Person: die aus der Gruppe.
    await expect.poll(async () => (await eintraege()).length).toBe(2);
    expect((await eintraege())[1]).toContain('Krause, Anna');

    await page.getByLabel(/Auch Leute außerhalb von „Lichtprobe"/).check();
    await expect.poll(async () => (await eintraege()).length).toBeGreaterThan(2);
    expect((await eintraege()).join('\n')).toContain('Fischer, David');

    await page.getByRole('dialog').getByRole('button', { name: 'Schließen' }).click();
  } finally {
    await api.delete(`/api/calendar/events/${termin.id}`);
    await api.delete(`/api/personnel/duty-templates/${generalprobe.id}`);
    await api.delete(`/api/access/groups/${gruppe.id}`);
  }
});
