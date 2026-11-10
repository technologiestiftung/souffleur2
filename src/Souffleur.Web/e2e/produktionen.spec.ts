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

/** Das aufgeschlagene Buch -- eine Produktion mit allen Abschnitten. */
function buch(page: Page, titel: string) {
  return page.getByRole('article', { name: `Produktionsbuch ${titel}` });
}

test('die Vorführdaten kennen Hamlet mit Besetzung, Budget, Bedarf und allem, was dranhängt', async ({
  page,
}) => {
  const errors = collectErrors(page);
  await signIn(page);

  // Der Reiter steht vor Touring: Die Schnittstelle liefert die Module in
  // Reiterreihenfolge, und Touring ist in einer frischen Installation aus.
  const module = (await (await page.request.get('/api/modules')).json()) as { key: string }[];
  const keys = module.map((m) => m.key);
  expect(keys.indexOf('Production')).toBeGreaterThan(keys.indexOf('Documents'));
  expect(keys.indexOf('Production')).toBeLessThan(keys.indexOf('Touring'));

  await page
    .getByRole('navigation', { name: 'Module' })
    .getByRole('link', { name: 'Produktionen' })
    .click();
  await expect(page).toHaveURL(/produktionen/);

  const register = page.getByRole('navigation', { name: 'Produktionen' });
  // Jede Zeile trägt ihren Herzschlag.
  await expect(
    register.getByRole('img', { name: /Aktivität|Keine Aktivität/ }).first(),
  ).toBeVisible();
  await register.getByRole('button', { name: /^Hamlet/ }).click();

  const hamlet = buch(page, 'Hamlet');
  await expect(hamlet.getByRole('region', { name: 'Besetzung' })).toContainText('Behrens, Anja');
  await expect(hamlet.getByRole('region', { name: 'Besetzung' })).toContainText('Ophelia');
  await expect(hamlet.getByRole('region', { name: 'Besetzung' })).toContainText('offen');
  await expect(hamlet.getByRole('region', { name: 'Budget' })).toContainText('Gagen Ensemble');
  await expect(hamlet.getByRole('region', { name: 'Budget' })).toContainText('18.000');
  await expect(hamlet.getByRole('region', { name: 'Bedarf' })).toContainText('Degen');
  await expect(hamlet.getByRole('region', { name: 'Dokumente' })).toContainText(
    'Aufführungsvertrag',
  );
  await expect(hamlet.getByRole('region', { name: 'Aufgaben' })).toContainText(
    'Premiere Hamlet vorbereiten',
  );
  await expect(hamlet.getByRole('region', { name: 'Termine' })).toContainText('Probe Hamlet');
  await expect(hamlet.getByRole('region', { name: 'Finanzen' })).toContainText('Honorar Gastregie');
  await expect(hamlet.getByRole('region', { name: 'Lesezeichen' })).toContainText('Suhrkamp');

  // Blättern: der Pfeil schlägt die nächste Seite auf, die Adresse folgt.
  const nextButton = page.getByRole('button', { name: 'Nächste Produktion' });
  const prevButton = page.getByRole('button', { name: 'Vorige Produktion' });
  if (await nextButton.isEnabled()) {
    await nextButton.click();
    await expect(hamlet).toHaveCount(0);
    await expect(page).toHaveURL(/produktion=/);
    await prevButton.click();
  } else {
    await prevButton.click();
    await expect(hamlet).toHaveCount(0);
    await nextButton.click();
  }
  await expect(buch(page, 'Hamlet')).toBeVisible();

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('eine neue Produktion bekommt Rollen, Positionen und eine Aufgabe und steht im Touring zur Wahl', async ({
  page,
}) => {
  const errors = collectErrors(page);
  await signIn(page);

  const stamp = Date.now();
  const titel = `Woyzeck ${stamp}`;

  await page.goto('/produktionen');
  await page.getByRole('button', { name: 'Produktion anlegen' }).first().click();
  const bogen = page.getByRole('dialog', { name: 'Produktion anlegen' });
  await bogen.getByLabel('Titel', { exact: true }).fill(titel);
  await bogen.getByLabel('Untertitel').fill('Georg Büchner');
  await bogen.getByLabel('Stand').selectOption({ label: 'In Planung' });
  await bogen.getByLabel('Budget', { exact: true }).fill('9000');
  await bogen.getByLabel('Beschreibung').fill('Fragment in 27 Szenen.');
  await bogen.getByRole('button', { name: 'Speichern' }).click();

  // Das Buch schlägt die neue Produktion auf.
  const seite = buch(page, titel);
  await expect(seite).toBeVisible();
  await expect(seite.getByRole('region', { name: 'Beschreibung' })).toContainText(
    'Fragment in 27 Szenen',
  );
  await expect(page.getByRole('heading', { level: 2 })).toContainText('Georg Büchner');

  // Rollen: eine offene, eine besetzte.
  // Das Plus öffnet den Bogen gleich mit einer leeren Zeile.
  await seite.getByRole('button', { name: 'Rolle hinzufügen' }).click();
  const besetzung = page.getByRole('dialog', { name: 'Besetzung' });
  await besetzung.getByLabel('Rolle', { exact: true }).nth(0).fill('Woyzeck');
  await besetzung.getByRole('button', { name: 'Rolle hinzufügen' }).click();
  await besetzung.getByLabel('Rolle', { exact: true }).nth(1).fill('Marie');
  await besetzung.getByLabel('Besetzt mit').nth(1).selectOption({ index: 1 });
  await besetzung.getByRole('button', { name: 'Speichern' }).click();
  await expect(seite.getByRole('region', { name: 'Besetzung' })).toContainText('1 von 2 besetzt');
  await expect(seite.getByRole('region', { name: 'Besetzung' })).toContainText('Woyzeck');

  // Positionen: die Summe steht gegen das Budget.
  await seite.getByRole('button', { name: 'Position hinzufügen' }).click();
  const budget = page.getByRole('dialog', { name: 'Budgetpositionen' });
  await budget.getByLabel('Bezeichnung der Position').fill('Gagen');
  await budget.getByLabel('Bereich der Position').fill('Gagen');
  await budget.getByLabel('Betrag der Position').fill('5000');
  await expect(budget).toContainText('von 9.000');
  await budget.getByRole('button', { name: 'Speichern' }).click();
  await expect(seite.getByRole('region', { name: 'Budget' })).toContainText('5.000');
  await expect(seite.getByRole('region', { name: 'Budget' })).toContainText('4.000');

  // Eine Aufgabe direkt aus dem Buch: Das Plus öffnet das Feld.
  await seite.getByRole('button', { name: 'Aufgabe hinzufügen' }).click();
  await seite.getByLabel('Neue Aufgabe').fill(`Bühnenbild ${stamp}`);
  await seite.getByRole('button', { name: 'Hinzufügen', exact: true }).click();
  await expect(seite.getByRole('region', { name: 'Aufgaben' })).toContainText(
    `Bühnenbild ${stamp}`,
  );

  // Ein Termin aus dem Buch: der Kalenderdialog, Produktion schon im Feld.
  await seite.getByRole('button', { name: 'Termin hinzufügen' }).click();
  const termin = page.getByRole('dialog');
  await expect(termin.getByLabel('Produktion')).toHaveValue(/.+/);
  await termin.getByLabel('Titel', { exact: true }).fill(`Leseprobe ${stamp}`);
  await termin.getByRole('button', { name: 'Anlegen', exact: true }).click();
  await expect(seite.getByRole('region', { name: 'Termine' })).toContainText(`Leseprobe ${stamp}`);

  // Ein Beleg aus dem Buch: Betrag und Beschreibung, zugeordnet.
  await seite.getByRole('button', { name: 'Beleg hinzufügen' }).click();
  const beleg = page.getByRole('dialog', { name: 'Beleg zur Produktion' });
  await beleg.getByLabel('Betrag').fill('250');
  await beleg.getByLabel('Beschreibung').fill(`Textbücher ${stamp}`);
  await beleg.getByRole('button', { name: 'Speichern' }).click();
  await expect(seite.getByRole('region', { name: 'Finanzen' })).toContainText(
    `Textbücher ${stamp}`,
  );
  await expect(seite.getByRole('region', { name: 'Budget' })).toContainText('250');

  // Die Überschrift führt ins Modul.
  await seite.getByRole('link', { name: 'Aufgaben' }).click();
  await expect(page).toHaveURL(/aufgaben/);
  await expect(page.getByText(`Bühnenbild ${stamp}`)).toBeVisible();
  await page.goBack();

  // Die Suche findet sie und springt hin.
  const suche = page.getByRole('searchbox', { name: 'Suchen in souffleur' });
  await suche.fill(`Woyzeck ${stamp}`);
  const treffer = page.getByRole('listbox', { name: 'Suchergebnisse' });
  await expect(treffer.getByRole('group', { name: 'Produktionen' })).toBeVisible();

  // Touring wählt aus den Produktionen -- über die Schnittstelle, der Reiter
  // ist in einer frischen Installation aus.
  const productions = (await (await page.request.get('/api/productions')).json()) as {
    id: string;
    title: string;
  }[];
  const neu = productions.find((p) => p.title === titel);
  expect(neu).toBeTruthy();

  await page.request.put('/api/modules/Touring', { data: { isEnabled: true, note: null } });
  const tour = await page.request.post('/api/touring/tours', {
    data: { title: `Tournee ${stamp}`, productionId: neu!.id },
  });
  expect(tour.ok(), await tour.text()).toBeTruthy();
  expect(((await tour.json()) as { productionLabel: string }).productionLabel).toBe(titel);

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('mit Anhang wird eine Produktion nicht gelöscht, sondern bleibt stehen', async ({ page }) => {
  await signIn(page);

  const stamp = Date.now();
  const created = await page.request.post('/api/productions', {
    data: { title: `Faust ${stamp}` },
  });
  const id = ((await created.json()) as { id: string }).id;
  const task = await page.request.post('/api/tasks', {
    data: { title: `Teufelspakt ${stamp}`, productionId: id },
  });
  expect(task.ok(), await task.text()).toBeTruthy();

  // Der Sprung aus der Adresse schlägt die Seite auf.
  await page.goto(`/produktionen?produktion=${id}`);
  const seite = buch(page, `Faust ${stamp}`);
  await expect(seite.getByRole('region', { name: 'Aufgaben' })).toContainText(
    `Teufelspakt ${stamp}`,
  );

  // Erst die Nachfrage, dann die Antwort des Servers: Mit Anhang wird nicht gelöscht.
  await page.getByRole('button', { name: `Produktion Faust ${stamp} löschen` }).click();
  const nachfrage = page.getByRole('dialog', { name: /löschen\?/ });
  await expect(nachfrage).toContainText('1 Aufgaben');
  await nachfrage.getByRole('button', { name: 'Endgültig löschen' }).click();
  await expect(nachfrage.getByRole('alert')).toContainText('abgespielt');

  // Abspielen ist der Weg: Die Produktion bleibt, mit allem, was an ihr hängt.
  await nachfrage.getByRole('button', { name: 'Abspielen' }).click();
  await expect(nachfrage).toHaveCount(0);
  await expect(seite).toBeVisible();
  await expect(page.getByRole('heading', { level: 2 }).locator('..')).toContainText('Abgespielt');
});

test('vom Kontakt aus wird jemand in eine Produktion besetzt', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);

  const stamp = Date.now();
  const created = await page.request.post('/api/productions', {
    data: { title: `Leonce und Lena ${stamp}` },
  });
  expect(created.ok(), await created.text()).toBeTruthy();

  await page.goto('/kontakte');
  await page.getByRole('row', { name: /Weber, Clara/ }).click();
  const bogen = page.getByRole('dialog', { name: /Weber, Clara/ });
  const produktionen = bogen.getByRole('group', { name: 'Produktionen' });
  // Aus den Vorführdaten: Clara spielt den Hamlet.
  await expect(produktionen).toContainText('Hamlet');

  await produktionen.getByLabel('Produktion', { exact: true }).selectOption({
    label: `Leonce und Lena ${stamp}`,
  });
  await produktionen.getByLabel('Rolle in der Produktion').fill('Lena');
  await produktionen.getByRole('button', { name: 'In Produktion besetzen' }).click();
  const besetzungen = produktionen.getByRole('list');
  await expect(besetzungen).toContainText(`Leonce und Lena ${stamp}`);
  await expect(besetzungen).toContainText('Lena');

  // Und wieder heraus -- die Vorführdaten bleiben, wie sie waren.
  await produktionen
    .getByRole('button', { name: `Rolle Lena in Leonce und Lena ${stamp} entfernen` })
    .click();
  await expect(besetzungen).not.toContainText(`Leonce und Lena ${stamp}`);

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});
