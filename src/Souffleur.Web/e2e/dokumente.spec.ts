import { expect, test, type Page } from '@playwright/test';

const ADMIN = { email: 'admin@localhost', password: 'Entwicklung1234' };

async function signIn(page: Page): Promise<void> {
  await page.goto('/anmelden');
  await page.getByLabel('E-Mail-Adresse').fill(ADMIN.email);
  await page.getByLabel('Passwort').fill(ADMIN.password);
  await page.getByRole('button', { name: 'Anmelden' }).click();
  await expect(page).not.toHaveURL(/anmelden/);
}

/** Eine Datei, die es nur im Speicher gibt. */
function datei(name: string, inhalt: string, mimeType = 'application/pdf') {
  return { name, mimeType, buffer: Buffer.from(inhalt) };
}

test('ein Dokument wird abgelegt, beschriftet, verknüpft und lässt sich herunterladen', async ({ page }) => {
  await signIn(page);
  await page.getByRole('navigation', { name: 'Module' }).getByRole('link', { name: 'Dokumente' }).click();
  await expect(page).toHaveURL(/dokumente/);

  const name = `Gastspielvertrag ${Date.now()}`;
  await page.getByLabel('Dokumentdatei').setInputFiles(datei('vertrag.pdf', `%PDF-1.4 ${name}`));

  const fenster = page.getByRole('dialog', { name: 'Dokument ablegen' });
  await fenster.getByLabel('Name').fill(name);
  await fenster.getByLabel(/Schlagworte/).fill('Vertrag, Gastspiel');
  await fenster.getByRole('button', { name: 'Ablegen' }).click();
  await expect(fenster).toHaveCount(0);

  const zeile = page.getByRole('row').filter({ hasText: name });
  await expect(zeile).toBeVisible();
  await expect(zeile.getByRole('button', { name: 'vertrag', exact: true })).toBeVisible();

  // Herunterladen liefert die Datei als Anhang, nicht als Seite.
  const [download] = await Promise.all([
    page.waitForEvent('download'),
    zeile.getByRole('link', { name: `${name} herunterladen` }).click(),
  ]);
  expect(download.suggestedFilename()).toBe(`${name}.pdf`);

  // Verknüpfen mit einem Kontakt.
  await zeile.getByRole('button', { name: `${name} ändern` }).click();
  const aendern = page.getByRole('dialog', { name: 'Dokument ändern' });
  await aendern.getByLabel('Art der Verknüpfung').selectOption({ label: 'Kontakt' });
  const auswahl = aendern.getByLabel('Verknüpfen mit');
  await expect(auswahl.locator('option').nth(1)).toBeAttached();
  const kontakt = (await auswahl.locator('option').nth(1).textContent())!.trim();
  await auswahl.selectOption({ index: 1 });
  await aendern.getByRole('button', { name: 'Verknüpfen' }).click();
  // In der Liste der Verknüpfungen -- derselbe Name steht auch in der Auswahl.
  await expect(aendern.getByRole('listitem').filter({ hasText: kontakt })).toBeVisible();
  await aendern.getByRole('button', { name: 'Speichern' }).click();

  await expect(zeile.getByText(kontakt)).toBeVisible();

  // Nach Schlagwort filtern.
  await zeile.getByRole('button', { name: 'gastspiel', exact: true }).click();
  await expect(page.getByRole('row').filter({ hasText: name })).toBeVisible();

  // Aufräumen
  await page.getByRole('row').filter({ hasText: name }).getByRole('button', { name: `${name} ändern` }).click();
  await page.getByRole('button', { name: 'Wegwerfen' }).click();
  await expect(page.getByRole('row').filter({ hasText: name })).toHaveCount(0);
});

test('ein eingeschränktes Dokument zeigt seine Gruppe', async ({ page }) => {
  await signIn(page);
  const gruppe = (await (
    await page.request.post('/api/access/groups', { data: { name: 'Personalakte' } })
  ).json()) as { id: string };

  try {
    await page.goto('/dokumente');
    const name = `Arbeitsvertrag ${Date.now()}`;
    await page.getByLabel('Dokumentdatei').setInputFiles(datei('vertrag.pdf', `%PDF-1.4 ${name}`));

    const fenster = page.getByRole('dialog', { name: 'Dokument ablegen' });
    await fenster.getByLabel('Name').fill(name);
    await fenster.getByLabel('nur bestimmte Gruppen').check();

    // Ohne Gruppe geht es nicht -- eingeschränkt auf niemanden ist ein Versehen.
    await fenster.getByRole('button', { name: 'Ablegen' }).click();
    await expect(fenster.getByText(/mindestens eine Gruppe/)).toBeVisible();

    await fenster.getByRole('checkbox', { name: 'Personalakte' }).check();
    await fenster.getByRole('button', { name: 'Ablegen' }).click();

    const zeile = page.getByRole('row').filter({ hasText: name });
    await expect(zeile.getByText('Personalakte')).toBeVisible();

    await zeile.getByRole('button', { name: `${name} ändern` }).click();
    await page.getByRole('button', { name: 'Wegwerfen' }).click();
    await expect(zeile).toHaveCount(0);
  } finally {
    await page.request.delete(`/api/access/groups/${gruppe.id}`);
  }
});

test('was der Browser ausführen würde, kommt nicht in die Ablage', async ({ page }) => {
  await signIn(page);

  const antwort = await page.request.post('/api/documents', {
    multipart: {
      file: { name: 'seite.html', mimeType: 'text/html', buffer: Buffer.from('<script>alert(1)</script>') },
    },
  });

  expect(antwort.status()).toBe(400);
  expect(await antwort.text()).toContain('gehören nicht in die Ablage');
});

test('mehrere Dateien auf einmal, Filter links und die Vorschau', async ({ page }) => {
  await signIn(page);
  await page.goto('/dokumente');

  const kennung = `Stapel ${Date.now()}`;
  // Ein echtes, winziges PNG -- die Vorschau soll es zeigen können.
  const png = Buffer.from(
    'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
    'base64',
  );
  await page.getByLabel('Dokumentdatei').setInputFiles([
    { name: `${kennung} Foto.png`, mimeType: 'image/png', buffer: png },
    datei(`${kennung} Programm.pdf`, `%PDF-1.4 ${kennung}`),
    datei(`${kennung} Notiz.txt`, `Probenplan ${kennung}`, 'text/plain'),
  ]);

  const fenster = page.getByRole('dialog');
  await expect(fenster.getByRole('list', { name: 'Dateien' }).getByRole('listitem')).toHaveCount(3);
  // Eine wieder herausnehmen.
  await fenster.getByRole('button', { name: `${kennung} Notiz.txt nicht ablegen` }).click();
  await fenster.getByLabel(/Schlagworte/).fill('stapeltest');
  await fenster.getByRole('button', { name: '2 Dateien ablegen' }).click();
  await expect(fenster).toHaveCount(0);

  const zeilen = page.getByRole('row').filter({ hasText: kennung });
  await expect(zeilen).toHaveCount(2);

  // Links filtern: nur Bilder.
  const leiste = page.getByRole('complementary', { name: 'Aktionen' });
  await leiste.getByRole('list', { name: 'Dateityp' }).getByRole('button', { name: /^Bilder/ }).click();
  await expect(zeilen).toHaveCount(1);
  await expect(zeilen.first()).toContainText('Foto');
  await page.getByRole('button', { name: 'Filter Bilder aufheben' }).click();
  await expect(zeilen).toHaveCount(2);

  // Die Vorschau zeigt das Bild und blättert mit der Pfeiltaste.
  await zeilen.filter({ hasText: 'Foto' }).getByRole('button', { name: `${kennung} Foto`, exact: true }).click();
  const vorschau = page.getByRole('dialog', { name: `Vorschau: ${kennung} Foto` });
  await expect(vorschau.getByRole('img', { name: `${kennung} Foto` })).toBeVisible();

  // Die Vorschau-Adresse liefert das Bild zum Anzeigen, nicht als Anhang.
  const bildUrl = await vorschau.getByRole('img', { name: `${kennung} Foto` }).getAttribute('src');
  const antwort = await page.request.get(bildUrl!);
  expect(antwort.headers()['content-disposition']).toMatch(/^inline/);

  await page.keyboard.press('Escape');
  await expect(vorschau).toHaveCount(0);

  // Aufräumen
  for (const teil of ['Foto', 'Programm']) {
    await zeilen.filter({ hasText: teil }).getByRole('button', { name: /ändern$/ }).click();
    await page.getByRole('button', { name: 'Wegwerfen' }).click();
  }
  await expect(zeilen).toHaveCount(0);
});
