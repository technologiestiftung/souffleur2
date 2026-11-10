/**
 * Die erste Seite eines PDFs als Bild -- für Vorschaubilder in Listen.
 *
 * pdf.js ist groß; es wird erst geladen, wenn tatsächlich ein PDF in der
 * Liste steht. Gerechnet wird im Hintergrund (Worker), höchstens zwei auf
 * einmal: Eine Liste mit fünfzig Programmheften soll den Rechner nicht
 * lahmlegen. Was einmal gerechnet ist, bleibt für die Sitzung im Speicher.
 */

type PdfJs = typeof import('pdfjs-dist');

let bibliothek: Promise<PdfJs> | null = null;
const fertig = new Map<string, Promise<string | null>>();
const warteschlange: (() => void)[] = [];
let laufend = 0;
const GLEICHZEITIG = 2;

function laden(): Promise<PdfJs> {
  bibliothek ??= import('pdfjs-dist').then((pdfjs) => {
    // Als feste Datei ausgeliefert (angular.json, Ausgabe „pdfjs") -- der
    // Bundler löst den Worker aus dem Paket nicht selbst auf.
    pdfjs.GlobalWorkerOptions.workerSrc = '/pdfjs/pdf.worker.min.mjs';
    return pdfjs;
  });
  return bibliothek;
}

async function reihum<T>(arbeit: () => Promise<T>): Promise<T> {
  if (laufend >= GLEICHZEITIG) {
    await new Promise<void>((weiter) => warteschlange.push(weiter));
  }
  laufend++;
  try {
    return await arbeit();
  } finally {
    laufend--;
    warteschlange.shift()?.();
  }
}

/** Liefert eine Bild-Adresse (data:) der ersten Seite, oder null, wenn es nicht geht. */
export function pdfVorschau(url: string, breite = 160): Promise<string | null> {
  const schluessel = `${url}#${breite}`;
  let ergebnis = fertig.get(schluessel);
  if (!ergebnis) {
    ergebnis = reihum(async () => {
      try {
        const pdfjs = await laden();
        const dokument = await pdfjs.getDocument({ url, withCredentials: true }).promise;
        try {
          const seite = await dokument.getPage(1);
          const grundmass = seite.getViewport({ scale: 1 });
          // Doppelte Auflösung für scharfe Bilder auf Retina-Bildschirmen.
          const ansicht = seite.getViewport({ scale: (breite * 2) / grundmass.width });
          const leinwand = document.createElement('canvas');
          leinwand.width = Math.ceil(ansicht.width);
          leinwand.height = Math.ceil(ansicht.height);
          const zeichner = leinwand.getContext('2d');
          if (!zeichner) {
            return null;
          }
          await seite.render({ canvasContext: zeichner, viewport: ansicht }).promise;
          return leinwand.toDataURL('image/jpeg', 0.8);
        } finally {
          void dokument.destroy();
        }
      } catch {
        return null;
      }
    });
    fertig.set(schluessel, ergebnis);
  }
  return ergebnis;
}
