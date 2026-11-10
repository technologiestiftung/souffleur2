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

test('die Aufgabenliste zeigt, was offen ist', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);

  await page
    .getByRole('navigation', { name: 'Module' })
    .getByRole('link', { name: 'Aufgaben' })
    .click();
  await expect(page).toHaveURL(/aufgaben/);

  await expect(page.getByText('Förderantrag Hauptstadtkulturfonds')).toBeVisible();

  // Die Checkliste hängt unter ihrer Aufgabe.
  await expect(page.getByText('Premiere Hamlet vorbereiten')).toBeVisible();
  await expect(page.getByText('Pressetext verschicken')).toBeVisible();

  // Erledigtes steht erst da, wenn man danach fragt.
  await expect(page.getByText('Spielzeitheft verschickt')).toHaveCount(0);
  await page.getByText('Erledigtes zeigen').click();
  await expect(page.getByText('Spielzeitheft verschickt')).toBeVisible();

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('eine Aufgabe lässt sich anlegen und abhaken', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await page.goto('/aufgaben');

  const title = `Fundus sortieren ${Date.now()}`;

  await page.getByRole('button', { name: 'Aufgabe anlegen' }).first().click();
  await page.getByLabel('Titel').fill(title);
  await page.getByRole('button', { name: 'Speichern' }).click();

  await expect(page.getByText(title)).toBeVisible();

  await page.getByRole('checkbox', { name: `${title} erledigen` }).check();

  // Abgehakt verschwindet sie aus der Liste der offenen.
  await expect(page.getByText(title)).toHaveCount(0);

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

/**
 * Mehr als Titel und Frist: Wer mitarbeitet, woran sie hängt, was man dazu
 * braucht. Beteiligte und Lesezeichen lassen sich schon beim Anlegen
 * eintragen -- die Aufgabe gibt es da noch nicht, angeheftet wird nach dem
 * Speichern.
 */
test('Beteiligte, Kostenstelle, Dokument und Lesezeichen hängen am Bogen', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await page.goto('/aufgaben');

  const title = `Technical Rider prüfen ${Date.now()}`;
  await page.getByRole('button', { name: 'Aufgabe anlegen' }).first().click();
  const bogen = page.getByRole('dialog', { name: 'Aufgabe anlegen' });
  await bogen.getByLabel('Titel').fill(title);

  // Beteiligt: ein zweiter Name neben dem Zuständigen.
  await bogen.getByLabel('Beteiligt hinzufügen').selectOption({ label: 'Schröder, Ben' });
  await expect(
    bogen.getByRole('list', { name: 'Beteiligt' }).getByText('Schröder, Ben'),
  ).toBeVisible();

  // Kostenstelle aus der Buchhaltung.
  await bogen.getByLabel('Kostenstelle').selectOption({ label: '100 Verwaltung' });

  // Ein Lesezeichen ohne Schema -- https:// kommt von selbst dazu.
  await bogen.getByLabel('Adresse').fill('youtube.com/watch?v=rider');
  await bogen.getByLabel('Stichwort').fill('Trailer');
  await bogen.getByRole('button', { name: 'Lesezeichen hinzufügen' }).click();
  await expect(
    bogen.getByRole('list', { name: 'Lesezeichen' }).getByRole('link', { name: 'Trailer' }),
  ).toBeVisible();

  // Eine Datei, die erst nach dem Speichern hochgeladen wird.
  await bogen.getByLabel('Datei wählen').setInputFiles({
    name: 'rider.txt',
    mimeType: 'text/plain',
    buffer: Buffer.from('4 Stangen, 12 Scheinwerfer'),
  });
  await expect(bogen.getByText('wird beim Speichern hochgeladen')).toBeVisible();

  await bogen.getByRole('button', { name: 'Speichern' }).click();
  await expect(bogen).toHaveCount(0);

  // In der Zeile: ein Beteiligter mehr, zwei Anhänge (Dokument + Lesezeichen).
  const zeile = page
    .locator('div', { has: page.getByRole('button', { name: title, exact: true }) })
    .last();
  await expect(zeile.getByTitle('Beteiligt: Schröder, Ben')).toHaveText('+1');
  await expect(zeile.getByTitle('2 Dokumente oder Lesezeichen')).toBeVisible();

  // Wieder geöffnet steht alles da -- und der Link geht in ein neues Fenster.
  await page.getByRole('button', { name: title, exact: true }).click();
  const aendern = page.getByRole('dialog', { name: 'Aufgabe ändern' });
  await expect(
    aendern.getByRole('list', { name: 'Beteiligt' }).getByText('Schröder, Ben'),
  ).toBeVisible();
  await expect(aendern.getByLabel('Kostenstelle')).toHaveValue(/./);
  const link = aendern
    .getByRole('list', { name: 'Lesezeichen' })
    .getByRole('link', { name: 'Trailer' });
  await expect(link).toHaveAttribute('href', 'https://youtube.com/watch?v=rider');
  await expect(link).toHaveAttribute('target', '_blank');
  await expect(
    aendern
      .getByRole('list', { name: 'Dokumente' })
      .getByRole('link', { name: 'rider', exact: true }),
  ).toBeVisible();

  // Die Frist der Aufgabe stünde auch im Kalender der Beteiligten: der
  // Personenfilter kennt Ben als Beteiligten (geprüft im Dienst-Test).
  await aendern.getByRole('button', { name: 'Abbrechen' }).click();

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('eine Frist steht im Kalender, gehört aber den Aufgaben', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await page.goto('/aufgaben');

  const title = `Abrechnung ${Date.now()}`;
  const due = new Date();
  due.setDate(due.getDate() + 3);
  const iso = due.toISOString().slice(0, 10);

  await page.getByRole('button', { name: 'Aufgabe anlegen' }).first().click();
  await page.getByLabel('Titel').fill(title);
  await page.getByLabel('Frist').fill(iso);
  await page.getByRole('button', { name: 'Speichern' }).click();
  await expect(page.getByText(title)).toBeVisible();

  await page.goto('/kalender');
  await page.getByRole('button', { name: `Tag ${iso} zeigen` }).click();

  const row = page.getByRole('listitem').filter({ hasText: title });
  await expect(row).toBeVisible();

  // Sie ist als Frist gekennzeichnet und führt zurück zu den Aufgaben --
  // geändert wird sie dort, nicht hier.
  await expect(row.getByRole('link', { name: 'Frist' })).toBeVisible();

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('der Zeitstrahl zeigt Vorgänge mit Dauer und meldet verdrehte Reihenfolgen', async ({
  page,
}) => {
  const errors = collectErrors(page);
  await signIn(page);
  await page.goto('/aufgaben');

  await page.getByRole('button', { name: 'Zeitstrahl' }).click();

  // Zeit läuft nach rechts: Monatsköpfe über dem Band. Der Ausschnitt
  // beginnt im laufenden Monat, also haengt der Name vom Datum ab.
  const months = [
    'Jan',
    'Feb',
    'Mär',
    'Apr',
    'Mai',
    'Jun',
    'Jul',
    'Aug',
    'Sep',
    'Okt',
    'Nov',
    'Dez',
  ];
  const now = new Date();
  const label = `${months[now.getMonth()]} ${now.getFullYear()}`;
  await expect(page.getByText(label, { exact: true })).toBeVisible();

  // Im Vorführbestand ist eine Reihenfolge absichtlich verdreht: Der
  // Pressetext ist vor der Abnahme faellig, auf die er wartet.
  await expect(page.getByText('Reihenfolge geht nicht auf')).toBeVisible();
  await expect(page.getByText(/„Pressetext verschicken" beginnt, bevor/)).toBeVisible();

  // Was weder Anfang noch Frist hat, erscheint nicht -- wird aber gezählt.
  await expect(page.getByText(/ohne Zeitangabe/)).toBeVisible();

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('die Spannweite des Zeitstrahls lässt sich ändern', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await page.goto('/aufgaben');
  await page.getByRole('button', { name: 'Zeitstrahl' }).click();

  await expect(page.getByRole('button', { name: '3 Monate zeigen' })).toHaveAttribute(
    'aria-pressed',
    'true',
  );

  await page.getByRole('button', { name: '12 Monate zeigen' }).click();

  await expect(page.getByRole('button', { name: '12 Monate zeigen' })).toHaveAttribute(
    'aria-pressed',
    'true',
  );

  // Der Verwendungsnachweis liegt im Februar -- im Vierteljahr war er nicht
  // zu sehen, im Jahr schon.
  await expect(page.getByTitle(/Verwendungsnachweis einreichen ·/)).toBeVisible();

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});
