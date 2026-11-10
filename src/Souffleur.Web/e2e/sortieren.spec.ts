import { expect, test, type Page } from '@playwright/test';

const ADMIN = { email: 'admin@localhost', password: 'Entwicklung1234' };

async function signIn(page: Page): Promise<void> {
  await page.goto('/anmelden');
  await page.getByLabel('E-Mail-Adresse').fill(ADMIN.email);
  await page.getByLabel('Passwort').fill(ADMIN.password);
  await page.getByRole('button', { name: 'Anmelden' }).click();
  await expect(page).not.toHaveURL(/anmelden/);
}

/** Die Beschriftungen der Reiter, in der Reihenfolge, in der sie stehen. */
async function reiter(page: Page): Promise<string[]> {
  const texte = await page
    .getByRole('navigation', { name: 'Module' })
    .getByRole('link')
    .allInnerTexts();
  return texte.map((t) => t.trim());
}

/**
 * Die Reihenfolge haengt am Konto, nicht am Browser. Darum wird hier nach
 * jedem Test aufgeraeumt -- sonst faende der naechste Lauf die verschobene
 * Reihenfolge vor und pruefte etwas anderes, als er zu pruefen glaubt.
 */
test.afterEach(async ({ page }) => {
  for (const schluessel of [
    'module',
    'aktionen:Aktion',
    'listen',
    'uebersicht',
    'uebersicht:ausgeblendet',
  ]) {
    await page.request
      .delete(`/api/preferences/${encodeURIComponent(schluessel)}`)
      .catch(() => undefined);
  }
});

/**
 * Die Knöpfe der linken Leiste, von oben nach unten.
 *
 * Erst auf den ersten warten: allInnerTexts() wartet von sich aus nicht und
 * gäbe eine leere Liste zurück, solange die Seite ihre Aktionen noch anmeldet.
 */
async function knoepfe(page: Page): Promise<string[]> {
  const alle = page.locator('aside[aria-label="Aktionen"] button[data-sortier-id]');
  await expect(alle.first()).toBeVisible();
  return (await alle.allInnerTexts()).map((t) => t.trim());
}

test('ein Reiter laesst sich mit der Tastatur verschieben', async ({ page }) => {
  await signIn(page);

  const vorher = await reiter(page);
  expect(vorher.length).toBeGreaterThan(2);

  // Den zweiten nach vorn: Strg und Pfeil nach links.
  const zweiter = vorher[1];
  await page
    .getByRole('navigation', { name: 'Module' })
    .getByRole('link', { name: zweiter, exact: true })
    .focus();
  await page.keyboard.press('Control+ArrowLeft');

  // Das Umsortieren laeuft ueber ein Signal und das Speichern am Konto --
  // einen Zeichendurchlauf spaeter, nicht im selben Tastendruck.
  await expect.poll(async () => (await reiter(page))[0]).toBe(zweiter);
  expect((await reiter(page))[1]).toBe(vorher[0]);
});

test('die eigene Reihenfolge ueberlebt das Neuladen', async ({ page }) => {
  await signIn(page);

  const vorher = await reiter(page);
  const zweiter = vorher[1];

  await page
    .getByRole('navigation', { name: 'Module' })
    .getByRole('link', { name: zweiter, exact: true })
    .focus();
  await page.keyboard.press('Control+ArrowLeft');
  await expect.poll(async () => (await reiter(page))[0]).toBe(zweiter);

  // Das ist der Kern: Sie liegt am Konto, nicht im Browser.
  await page.reload();
  await expect.poll(async () => (await reiter(page))[0]).toBe(zweiter);
});

test('ein Reiter laesst sich mit der Maus an eine andere Stelle ziehen', async ({ page }) => {
  await signIn(page);

  const vorher = await reiter(page);
  const nav = page.getByRole('navigation', { name: 'Module' });
  const erster = nav.getByRole('link', { name: vorher[0], exact: true });
  const dritter = nav.getByRole('link', { name: vorher[2], exact: true });

  const von = await erster.boundingBox();
  const nach = await dritter.boundingBox();
  if (!von || !nach) {
    throw new Error('Die Reiter haben keine Ausdehnung.');
  }

  await page.mouse.move(von.x + von.width / 2, von.y + von.height / 2);
  await page.mouse.down();
  // In Schritten, nicht in einem Sprung: Unter der Schwelle gilt es als
  // Klick, und ein einzelner Sprung erzeugt nur ein pointermove.
  await page.mouse.move(nach.x + nach.width / 2, nach.y + nach.height / 2, { steps: 10 });
  await page.mouse.up();

  // Der erste steht jetzt hinter dem urspruenglich zweiten.
  await expect.poll(async () => (await reiter(page)).indexOf(vorher[0])).toBeGreaterThan(0);

  // Und der Zug hat nicht nebenbei die Seite gewechselt.
  await expect(page).not.toHaveURL(/anmelden/);
});

test('auch die Knoepfe links lassen sich umsortieren und bleiben so', async ({ page }) => {
  await signIn(page);
  await page.goto('/uebersicht');

  const vorher = await knoepfe(page);
  expect(vorher.length).toBeGreaterThan(2);

  // Senkrechte Leiste: nach oben statt nach links.
  const zweiter = vorher[1];
  await page.locator('aside[aria-label="Aktionen"] button[data-sortier-id]').nth(1).focus();
  await page.keyboard.press('Control+ArrowUp');

  await expect.poll(async () => (await knoepfe(page))[0]).toBe(zweiter);

  await page.reload();
  await expect.poll(async () => (await knoepfe(page))[0]).toBe(zweiter);
});

test('beim Ziehen weichen die anderen aus, und danach bleibt nichts stehen', async ({ page }) => {
  await signIn(page);

  const nav = page.getByRole('navigation', { name: 'Module' });
  const vorher = await reiter(page);
  const erster = nav.getByRole('link', { name: vorher[0], exact: true });
  const zweiter = nav.getByRole('link', { name: vorher[1], exact: true });
  const dritter = nav.getByRole('link', { name: vorher[2], exact: true });

  const von = await erster.boundingBox();
  const nach = await dritter.boundingBox();
  if (!von || !nach) {
    throw new Error('Die Reiter haben keine Ausdehnung.');
  }

  await page.mouse.move(von.x + von.width / 2, von.y + von.height / 2);
  await page.mouse.down();
  await page.mouse.move(nach.x + nach.width / 2, nach.y + nach.height / 2, { steps: 10 });

  // Mitten im Zug: Der gezogene Reiter haengt angehoben ueber den anderen,
  // und der zweite ist nach links gerueckt, um Platz zu machen.
  await expect(erster).toHaveCSS('z-index', '60');
  await expect
    .poll(async () => (await zweiter.evaluate((el) => el.style.transform)) || '')
    .toMatch(/translateX\(-\d/);

  await page.mouse.up();

  // Danach steht nichts mehr verschoben -- die Reihenfolge selbst hat sich
  // geaendert, nicht die Darstellung.
  await expect
    .poll(async () => (await zweiter.evaluate((el) => el.style.transform)) || '')
    .toBe('');
  await expect(page.locator('#souffleur-greifhand')).toHaveCount(0);
});

test('auch die Wertelisten lassen sich umsortieren', async ({ page }) => {
  await signIn(page);
  await page.goto('/einstellungen/listen');

  // Eine andere Bauart als die beiden Leisten: Die Knoepfe stecken in
  // <li>-Huellen, und die wandern beim Sortieren, nicht die Knoepfe.
  const eintraege = page.locator('nav[aria-label="Listen"] li[data-sortier-id]');
  await expect(eintraege.first()).toBeVisible();

  const vorher = (await eintraege.allInnerTexts()).map((t) => t.trim());
  expect(vorher.length).toBeGreaterThan(2);

  await eintraege.nth(1).getByRole('button').focus();
  await page.keyboard.press('Control+ArrowUp');

  await expect
    .poll(async () => (await eintraege.allInnerTexts()).map((t) => t.trim())[0])
    .toBe(vorher[1]);

  await page.reload();
  await expect(eintraege.first()).toBeVisible();
  await expect
    .poll(async () => (await eintraege.allInnerTexts()).map((t) => t.trim())[0])
    .toBe(vorher[1]);
});

test('die Eintraege einer Werteliste wandern am Griff -- auch mit der Tastatur', async ({
  page,
}) => {
  await signIn(page);
  await page.goto('/einstellungen/listen/EventCategory');

  // Nicht die Leiste mit den Listen, sondern die Zeilen der gewaehlten Liste:
  // Jede traegt vorn einen Griff, der fokussierbar ist.
  const zeilen = page.locator('tbody tr[data-sortier-id]');
  await expect(zeilen.first()).toBeVisible();

  const namen = async (): Promise<string[]> =>
    (await zeilen.locator('td:nth-child(2)').allInnerTexts()).map((t) => t.trim().split('\n')[0]);

  const vorher = await namen();
  expect(vorher.length).toBeGreaterThan(2);

  // Zweiten Eintrag eine Stelle nach oben -- ueber seinen Griff. Die Liste
  // folgt sofort, der Server danach: Erst wenn er geantwortet hat, darf
  // neu geladen werden, sonst sieht der Test die alte Reihenfolge.
  const gespeichert = () =>
    page.waitForResponse((antwort) => antwort.url().includes('/order') && antwort.ok());
  await zeilen
    .nth(1)
    .getByRole('button', { name: /verschieben$/ })
    .focus();
  let antwort = gespeichert();
  await page.keyboard.press('Control+ArrowUp');
  await expect.poll(async () => (await namen())[0]).toBe(vorher[1]);
  await antwort;

  // Der Server hat es: Nach dem Neuladen steht es noch so.
  await page.reload();
  await expect(zeilen.first()).toBeVisible();
  await expect.poll(async () => (await namen())[0]).toBe(vorher[1]);

  // Und zurueck, damit die Liste fuer alle anderen so bleibt, wie sie war.
  await zeilen
    .nth(0)
    .getByRole('button', { name: /verschieben$/ })
    .focus();
  antwort = gespeichert();
  await page.keyboard.press('Control+ArrowDown');
  await expect.poll(async () => (await namen())[0]).toBe(vorher[0]);
  await antwort;
});

/** Die Blöcke der Übersicht, von oben nach unten, bei ihrem Namen. */
async function bloecke(page: Page): Promise<string[]> {
  const alle = page.locator('section[data-sortier-id]');
  await expect(alle.first()).toBeVisible();
  return alle.evaluateAll((teile) => teile.map((t) => t.getAttribute('data-sortier-name') ?? ''));
}

test('ein Block der Uebersicht laesst sich verschieben, und das bleibt so', async ({ page }) => {
  await signIn(page);
  await page.goto('/uebersicht');

  const vorher = await bloecke(page);
  expect(vorher.length).toBeGreaterThan(2);

  // Den zweiten nach oben: am Griff, mit Strg und Pfeil nach oben. Erst
  // lauschen, dann druecken -- sonst ist die Antwort des Servers schon
  // vorbei, ehe jemand auf sie wartet.
  const zweiter = vorher[1];
  const gespeichert = page.waitForResponse(
    (antwort) => antwort.url().includes('/api/preferences/uebersicht') && antwort.ok(),
  );
  await page.getByRole('button', { name: `${zweiter} verschieben` }).focus();
  await page.keyboard.press('Control+ArrowUp');

  await expect.poll(async () => (await bloecke(page))[0]).toBe(zweiter);
  expect((await bloecke(page))[1]).toBe(vorher[0]);
  await gespeichert;

  // Am Konto, nicht im Browser: Nach dem Neuladen steht er noch oben.
  await page.reload();
  await expect.poll(async () => (await bloecke(page))[0]).toBe(zweiter);
});

test('ein Block der Uebersicht laesst sich ausblenden und hinterm Zahnrad wiederfinden', async ({
  page,
}) => {
  await signIn(page);
  await page.goto('/uebersicht');

  const vorher = await bloecke(page);
  const zweiter = vorher[1];

  // Aus dem Menü des Blocks heraus; die Antwort des Servers abwarten, ehe
  // die Seite neu geladen wird.
  const gemerkt = page.waitForResponse(
    (antwort) =>
      antwort.url().includes('/api/preferences/uebersicht%3Aausgeblendet') && antwort.ok(),
  );
  await page.getByRole('button', { name: `${zweiter}: weitere Einstellungen` }).click();
  await page.getByRole('menuitem', { name: 'Ausblenden' }).click();
  await expect.poll(() => bloecke(page)).not.toContain(zweiter);
  await gemerkt;

  await page.reload();
  await expect.poll(() => bloecke(page)).not.toContain(zweiter);

  // Hinterm Zahnrad steht er als ausgeblendet -- und ein Haken holt ihn zurück.
  await page.getByRole('button', { name: 'Übersicht einrichten' }).click();
  const feld = page.getByRole('region', { name: 'Übersicht einrichten' });
  await expect(feld.getByText('ausgeblendet', { exact: true })).toBeVisible();
  await feld.getByRole('checkbox', { name: new RegExp(`^${zweiter}`) }).check();
  await expect.poll(() => bloecke(page)).toContain(zweiter);
});
