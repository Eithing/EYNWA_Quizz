import { Injectable, computed, signal } from '@angular/core';

/**
 * Compteur global de chargement : incrémenté par l'intercepteur HTTP pour chaque requête
 * en vol, et par les événements de navigation du Router (utile pour le délai de chargement
 * des chunks lazy-loaded). Sert à afficher un overlay bloquant partout dans l'app sans avoir
 * à gérer un état "loading" dans chaque composant.
 */
@Injectable({ providedIn: 'root' })
export class LoadingService {
  private readonly httpActiveCount = signal(0);
  private readonly routeNavigating = signal(false);

  readonly isLoading = computed(() => this.httpActiveCount() > 0 || this.routeNavigating());

  startHttp(): void {
    this.httpActiveCount.update((count) => count + 1);
  }

  stopHttp(): void {
    this.httpActiveCount.update((count) => Math.max(0, count - 1));
  }

  setRouteNavigating(value: boolean): void {
    this.routeNavigating.set(value);
  }
}
