import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';

/**
 * Barrierefreiheit nach WCAG 2.1 AA, gemessen mit axe-core -- auf jeder
 * Hauptseite, im hellen und im dunklen Thema. So wie im Hausstandard der
 * Technologiestiftung (template-vite-react): Barrierefreiheit wird geprüft,
 * nicht über eine Komponentenbibliothek eingekauft.
 *
 * Gemessen wird der Zustand nach dem Laden. Was erst nach einem Klick
 * erscheint -- Bögen, Fenster, Menüs --, deckt das nicht ab.
 */

const ADMIN = { email: 'admin@localhost', password: 'Entwicklung1234' };
const REGELN = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'];

const SEITEN = [
  '/uebersicht',
  '/finanzen',
  '/finanzen/belege',
  '/finanzen/bank',
  '/personal',
  '/aufgaben',
  '/kontakte',
  '/kalender',
  '/dokumente',
  '/produktionen',
  '/ticketing',
  '/touring',
  '/einstellungen/haus',
  '/einstellungen/zugaenge',
  '/einstellungen/kontenplan',
];

/** Die Verstöße als lesbare Zeilen -- damit der Fehlschlag sagt, wo es hakt. */
async function pruefe(page: Page): Promise<string[]> {
  const ergebnis = await new AxeBuilder({ page }).withTags(REGELN).analyze();
  return ergebnis.violations.flatMap((v) =>
    v.nodes.map((n) => `${v.id} (${v.impact}): ${n.target.join(' ')}`),
  );
}

test('die Anmeldung ist barrierefrei', async ({ page }) => {
  await page.goto('/anmelden');
  await expect(page.getByRole('button', { name: 'Anmelden' })).toBeVisible();
  expect(await pruefe(page)).toEqual([]);
});

for (const thema of ['light', 'dark'] as const) {
  test(`die Hauptseiten sind barrierefrei (${thema === 'light' ? 'hell' : 'dunkel'})`, async ({
    page,
  }) => {
    test.setTimeout(120_000);
    await page.goto('/anmelden');
    await page.getByLabel('E-Mail-Adresse').fill(ADMIN.email);
    await page.getByLabel('Passwort').fill(ADMIN.password);
    await page.getByRole('button', { name: 'Anmelden' }).click();
    await expect(page).toHaveURL(/uebersicht/);

    const funde: string[] = [];
    for (const seite of SEITEN) {
      await page.goto(seite);
      await page.waitForLoadState('networkidle');
      await page.evaluate((t) => (document.documentElement.dataset['theme'] = t), thema);
      funde.push(...(await pruefe(page)).map((f) => `${seite}: ${f}`));
    }

    expect(funde, funde.join('\n')).toEqual([]);
  });
}
