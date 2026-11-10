import { expect, test, type Page } from '@playwright/test';

const ADMIN = { email: 'admin@localhost', password: 'Entwicklung1234' };

function collectErrors(page: Page, also?: string): string[] {
  const allowed = ['401', '403', '404', '409', ...(also ? [also] : [])].join('|');
  const expected = new RegExp(`Failed to load resource.*(${allowed})`);
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

/**
 * Ein Auszug, wie ihn eine Bank liefert. Der Namensraum wechselt je nach
 * Fassung — der Leser darf sich nicht daran festhalten.
 */
function statement(): string {
  return `<?xml version="1.0" encoding="UTF-8"?>
<Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.053.001.02">
  <BkToCstmrStmt>
    <GrpHdr><MsgId>M-1</MsgId></GrpHdr>
    <Stmt>
      <Id>2026-03</Id>
      <Acct><Id><IBAN>DE85999999990001234567</IBAN></Id></Acct>
      <Bal>
        <Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp>
        <Amt Ccy="EUR">4820.15</Amt>
        <CdtDbtInd>CRDT</CdtDbtInd>
        <Dt><Dt>2026-03-31</Dt></Dt>
      </Bal>
      <Ntry>
        <Amt Ccy="EUR">247.80</Amt>
        <CdtDbtInd>DBIT</CdtDbtInd>
        <Sts><Cd>BOOK</Cd></Sts>
        <BookgDt><Dt>2026-03-12</Dt></BookgDt>
        <AcctSvcrRef>REF-100</AcctSvcrRef>
        <NtryDtls><TxDtls>
          <RltdPties><Cdtr><Nm>Buehnenbedarf Schmidt GmbH</Nm></Cdtr></RltdPties>
          <RmtInf><Ustrd>Rechnung RE-2026-0412</Ustrd></RmtInf>
        </TxDtls></NtryDtls>
      </Ntry>
      <Ntry>
        <Amt Ccy="EUR">1500.00</Amt>
        <CdtDbtInd>CRDT</CdtDbtInd>
        <Sts><Cd>BOOK</Cd></Sts>
        <BookgDt><Dt>2026-03-20</Dt></BookgDt>
        <AcctSvcrRef>REF-101</AcctSvcrRef>
        <NtryDtls><TxDtls>
          <RltdPties><Dbtr><Nm>Senatsverwaltung</Nm></Dbtr></RltdPties>
          <RmtInf><Ustrd>Zuwendung erste Rate</Ustrd></RmtInf>
        </TxDtls></NtryDtls>
      </Ntry>
    </Stmt>
  </BkToCstmrStmt>
</Document>`;
}

async function upload(page: Page): Promise<void> {
  // Der Auszug geht durch das Fenster: Fläche zum Fallenlassen, daneben die
  // Dateiauswahl -- der Test nimmt den zweiten Weg.
  await page.getByRole('button', { name: 'Auszug einlesen' }).click();
  await page.getByLabel('Auszugsdatei').setInputFiles({
    name: 'auszug.xml',
    mimeType: 'text/xml',
    buffer: Buffer.from(statement()),
  });
  await page.getByRole('button', { name: 'Einlesen', exact: true }).click();
}

test('das Konto des Hauses steht da', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await page.goto('/finanzen/bank');

  await expect(page.getByRole('combobox').first()).toContainText('Geschäftskonto');

  // Was noch fehlt, steht auch da -- statt so zu tun, als gäbe es es.
  await expect(page.getByText(/Die Verbindung zur Bank selbst/)).toBeVisible();

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('ein Auszug lässt sich einlesen, zweimal ändert nichts', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await page.goto('/finanzen/bank');

  await upload(page);
  await expect(page.getByText(/2 neue Umsätze/)).toBeVisible();
  await expect(page.getByText('Zuwendung erste Rate')).toBeVisible();
  await expect(page.getByText(/Saldo/).first()).toBeVisible();

  // Niemand weiß, welche Datei schon dran war -- also darf es egal sein.
  await upload(page);
  await expect(page.getByText(/0 neue Umsätze/)).toBeVisible();
  await expect(page.getByText(/2 waren schon da/)).toBeVisible();

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});

test('ein Umsatz lässt sich dem Beleg zuordnen, der dazu passt', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await page.goto('/finanzen/bank');

  await upload(page);
  await expect(page.getByText('Rechnung RE-2026-0412')).toBeVisible();

  const row = page.getByRole('row').filter({ hasText: 'Rechnung RE-2026-0412' });
  await row.getByRole('button', { name: 'Beleg suchen' }).click();

  // Der Vorschlag nennt den Grund, nicht nur den Beleg.
  const suggestion = row.getByRole('button').filter({ hasText: 'Beleuchtungsmittel' });
  await expect(suggestion).toContainText('Rechnungsnummer im Verwendungszweck');

  await suggestion.click();
  await expect(row).toContainText('2026-00001');

  expect(errors, `Fehler in der Browserkonsole:\n${errors.join('\n')}`).toEqual([]);
});
