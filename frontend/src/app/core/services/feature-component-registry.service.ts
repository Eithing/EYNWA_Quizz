import { Injectable, Type } from '@angular/core';

/**
 * Mappe une clé de feature (ex: "zoom-image") vers ses composants Angular dédiés
 * (Editor / Player / AdminControl / Preview — section 4 de la spec).
 *
 * Vide pour l'instant : aucune feature n'a encore d'éditeur visuel dédié (Phase 1
 * édite la config en JSON brut). La feature Zoom Progressif enregistrera son
 * EditorComponent ici en Phase 2 ; en son absence, l'éditeur de manche retombe
 * sur le formulaire JSON générique.
 */
@Injectable({ providedIn: 'root' })
export class FeatureComponentRegistry {
  private readonly editors = new Map<string, Type<unknown>>();

  registerEditor(typeKey: string, component: Type<unknown>): void {
    this.editors.set(typeKey, component);
  }

  getEditor(typeKey: string): Type<unknown> | undefined {
    return this.editors.get(typeKey);
  }
}
