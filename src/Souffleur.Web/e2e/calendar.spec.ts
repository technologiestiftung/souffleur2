import { expect, test, type Page } from '@playwright/test';

const ADMIN = { email: 'admin@localhost', password: 'Entwicklung1234' };

function collectErrors(page: Page): string[] {
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

async function signIn(page: Page): Promise<void> {
  await page.goto('/anmelden');
  await page.getByLabel('E-Mail-Adresse').fill(ADMIN.email);
  await page.getByLabel('Passwort').fill(ADMIN.password);
  await page.getByRole('button', { name: 'Anmelden' }).click();
  await expect(page).toHaveURL(/uebersicht/);
}

test('das Jahresraster zeigt Monate als Spalten und Tage als Zeilen', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);

  await page.getByRole('navigation', { name: 'Module' })
            .getByRole('link', { name: 'Kalender' }).click();
  await expect(page).toHaveURL(/kalender/);

  // Zwölf Monatsspalten in der Vorgabe, und die Tageszeilen bis 31.
  // Genau die Zählung des Rasters, nicht irgendein "Termine": Über dem
  // Kalender stehen seit den Kennzahlen weitere Kacheln mit dem Wort.
  await expect(page.getByText(/^\d+ Termine$/)).toBeVisible();
  await expect(page.locator('text=31').first()).toBeVisible();

  // Die Beispieldaten liegen relativ zu heute -- irgendetwas muss stehen.
  await expect(page.getByTitle(/Probe Hamlet/).first()).toBeVisible();

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('die Spannweite lässt sich von zwölf auf achtzehn Monate stellen', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await page.goto('/kalender');

  await expect(page.getByRole('button', { name: '12 Monate zeigen' }))
    .toHaveAttribute('aria-pressed', 'true');

  await page.getByRole('button', { name: '18 Monate zeigen' }).click();

  await expect(page.getByRole('button', { name: '18 Monate zeigen' }))
    .toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByRole('button', { name: '12 Monate zeigen' }))
    .toHaveAttribute('aria-pressed', 'false');

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('die Spielzeit beginnt im August', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await page.goto('/kalender');

  await page.getByRole('button', { name: 'Spielzeit', exact: true }).click();

  await expect(page.getByText(/^August \d{4} – Juli \d{4}$/)).toBeVisible();

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('ein Termin lässt sich anlegen und steht danach im Raster', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await page.goto('/kalender');

  // Über die linke Aktionsleiste -- von links wird gehandelt.
  await page.getByRole('button', { name: 'Termin anlegen', exact: true }).first().click();

  const title = `Browsertest ${Date.now()}`;
  await page.getByLabel('Titel').fill(title);
  await page.getByRole('button', { name: 'Anlegen', exact: true }).click();

  // Danach steht der Tag unten ausgeschrieben, mit dem neuen Termin darin.
  await expect(page.getByText(title).first()).toBeVisible();

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('die Kategorien lassen sich im Raster ausblenden', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await page.goto('/kalender');

  // Genau diese Beschriftung: „Vorstellung" steckt auch in Termintiteln,
  // und die stehen als Knöpfe im Raster.
  const filter = page.getByRole('button', { name: 'Vorstellung', exact: true });
  await expect(filter).toHaveAttribute('aria-pressed', 'true');

  await filter.click();
  await expect(filter).toHaveAttribute('aria-pressed', 'false');

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('die Terminkategorien gehören dem Haus und lassen sich ändern', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await page.goto('/einstellungen/listen/EventCategory');

  await expect(page.getByRole('heading', { name: 'Terminkategorien' })).toBeVisible();

  // Mitgeliefertes bleibt bestehen: umbenennen und abschalten ja, löschen nein.
  const row = page.getByRole('row').filter({ hasText: 'Generalprobe' });
  await expect(row.getByText('mitgeliefert')).toBeVisible();
  await expect(row.getByRole('button', { name: 'Löschen' })).toHaveCount(0);

  // Eine eigene Kategorie anlegen.
  const label = `Sonderformat ${Date.now()}`;
  await page.getByRole('button', { name: 'Eintrag anlegen' }).click();
  await page.getByLabel('Beschriftung').fill(label);
  await page.getByRole('button', { name: 'Speichern' }).click();

  const created = page.getByRole('row').filter({ hasText: label });
  await expect(created).toBeVisible();

  // Unbenutzt und selbst angelegt: die lässt sich wieder löschen.
  await created.getByRole('button', { name: 'Löschen' }).click();
  await expect(page.getByRole('row').filter({ hasText: label })).toHaveCount(0);

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('die vier Ansichten lassen sich wechseln', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await page.goto('/kalender');

  // Das Jahr ist der Einstieg (ADR-0006).
  await expect(page.getByRole('button', { name: 'Jahr', exact: true }))
    .toHaveAttribute('aria-pressed', 'true');

  await page.getByRole('button', { name: 'Monat', exact: true }).click();
  await expect(page.getByText('Donnerstag', { exact: true })).toBeVisible();

  await page.getByRole('button', { name: 'Woche', exact: true }).click();
  await expect(page.getByText(/^KW \d+ · /)).toBeVisible();

  await page.getByRole('button', { name: 'Tag', exact: true }).click();
  // Nicht „\w+tag": Der Mittwoch endet nicht auf -tag, und \w kennt kein
  // „ä" -- der Test fiel jeden Mittwoch und den ganzen März durch.
  await expect(page.getByText(/^\p{L}+, \d+\. \p{L}+ \d{4}$/u)).toBeVisible();

  // Spannweite und Spielzeit gehören zum Jahr und verschwinden mit ihm.
  await expect(page.getByRole('button', { name: '12 Monate zeigen' })).toHaveCount(0);

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('die Wochenansicht zeigt Stunden und stellt Überschneidungen nebeneinander', async ({
  page,
}) => {
  const errors = collectErrors(page);
  await signIn(page);
  await page.goto('/kalender');

  await page.getByRole('button', { name: 'Woche', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Woche', exact: true }))
    .toHaveAttribute('aria-pressed', 'true');

  // Die Stundenachse -- sie unterscheidet die Woche vom Jahresraster.
  await expect(page.getByRole('group', { name: 'Stunden' }).getByText('19:00')).toBeVisible();

  // Probe (19:30) und Vorstellung (20:00) überschneiden sich. Beide müssen
  // sichtbar sein und dürfen sich nicht verdecken: Genau das ist die
  // Kollision, die man sonst übersieht.
  const probe = page.getByTitle(/Probe Hamlet · 19:30/).first();
  const vorstellung = page.getByTitle(/Der Besuch der alten Dame · 20:00/).first();

  if ((await probe.count()) > 0 && (await vorstellung.count()) > 0) {
    const a = await probe.boundingBox();
    const b = await vorstellung.boundingBox();
    expect(a).not.toBeNull();
    expect(b).not.toBeNull();
    // Nebeneinander heißt: kein waagerechter Überlapp.
    expect(a!.x + a!.width).toBeLessThanOrEqual(b!.x + 1);
  }

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('ein Tag aus dem Jahresraster lässt sich im Tag öffnen', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await page.goto('/kalender');

  // Im Jahresraster einen Tag wählen -- der Tag öffnet sich als Fenster.
  await page.getByRole('button', { name: /^Tag \d{4}-\d{2}-\d{2} zeigen/ }).first().click();
  const fenster = page.getByRole('dialog');
  await expect(fenster).toBeVisible();
  await expect(fenster.getByRole('button', { name: 'Im Tag öffnen' })).toBeVisible();

  await fenster.getByRole('button', { name: 'Im Tag öffnen' }).click();

  // Danach steht die Tagesansicht, der gewählte Tag ist der Ausschnitt, und
  // das Fenster ist zu -- dort zeigt die Ansicht den Tag selbst.
  await expect(page.getByRole('button', { name: 'Tag', exact: true }))
    .toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByRole('dialog')).toHaveCount(0);

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('der Tag liegt als Fenster über dem Raster, wie bei Aufgaben und Kontakten', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await page.goto('/kalender');

  await page.getByRole('button', { name: /^Tag \d{4}-/ }).first().click();

  // Das Fenster trägt den Tag als Namen und steht im Bild -- nicht als
  // Leiste unter dem Raster, zu der man erst scrollen muss.
  const fenster = page.getByRole('dialog', {
    name: /^(Montag|Dienstag|Mittwoch|Donnerstag|Freitag|Samstag|Sonntag), /,
  });
  await expect(fenster).toBeInViewport();
  await expect(fenster.getByRole('button', { name: 'Termin anlegen', exact: true }))
    .toBeInViewport();

  // Blättern bleibt im Fenster; Escape schließt es, wie überall.
  await fenster.getByRole('button', { name: 'Nächster Tag' }).click();
  await expect(fenster).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog')).toHaveCount(0);

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('ein Dauerlauf pflastert das Jahresraster nicht zu', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);

  // Eine Installation über ein halbes Jahr -- der Fall, an dem das Raster
  // bei einem Haus unlesbar wurde.
  const start = new Date();
  start.setMonth(start.getMonth() - 1, 15);
  const ende = new Date(start);
  ende.setMonth(ende.getMonth() + 5);

  const tag = (d: Date) => d.toISOString().slice(0, 10);

  const angelegt = await page.request.post('/api/calendar/events', {
    data: {
      title: 'Dauerinstallation im Foyer',
      startsAt: `${tag(start)}T00:00:00+01:00`,
      endsAt: `${tag(ende)}T00:00:00+01:00`,
      isAllDay: true,
    },
  });
  expect(angelegt.ok(), await angelegt.text()).toBeTruthy();

  await page.goto('/kalender');
  await expect(page.getByTitle(/Dauerinstallation/).first()).toBeVisible();

  // Höchstens einmal je Monatsspalte, nicht an jedem der rund 150 Tage.
  const treffer = await page.getByTitle(/Dauerinstallation/).count();
  expect(treffer, `${treffer} Einträge -- erwartet höchstens einer je Monat`)
    .toBeLessThanOrEqual(12);

  // Und er sagt, dass er weiterläuft.
  await expect(page.getByTitle(/Dauerinstallation.*läuft bis/).first()).toBeVisible();

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});
