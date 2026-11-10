import { DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, ParamMap } from '@angular/router';

/**
 * Ruft den Handler mit den Abfrageparametern der Adresse -- jetzt und bei
 * jeder Änderung, solange die Komponente lebt.
 *
 * Für Sprungziele aus Suche und Glocke: `/kontakte?kontakt=…` soll den
 * Kontakt öffnen. Ein Blick auf den Snapshot in ngOnInit reicht dafür nicht,
 * denn wer schon auf der Seite steht und einen zweiten Fund anklickt,
 * bekommt keine neue Komponente, nur eine neue Adresse.
 *
 * Erst nach dem Laden aufrufen: Die erste Zustellung kommt sofort, und der
 * Handler sucht sein Ziel in den geladenen Daten.
 */
export function verfolgeAdresse(
  route: ActivatedRoute,
  destroyRef: DestroyRef,
  handler: (params: ParamMap) => void | Promise<void>,
): void {
  route.queryParamMap
    .pipe(takeUntilDestroyed(destroyRef))
    .subscribe((params) => void handler(params));
}
