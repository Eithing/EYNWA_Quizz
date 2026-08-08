import { Signal, WritableSignal, effect, signal } from '@angular/core';
import { ExpectedAnswerDraft } from './expected-answers-editor/expected-answers-editor.component';

export type PointsMode = 'Uniform' | 'PerAnswer';

/** Garde un signal `payload` synchronisé avec l'input `payloadJson` : reparse à chaque changement, avec
 * repli sur `defaultPayload()` si le JSON est absent/invalide. `transform` permet une migration douce
 * post-parse (ex. reconstruire expectedAnswers depuis l'ancien acceptedAnswers). Appelé en initialiseur
 * de champ (avant le corps du constructeur) : le contexte d'injection Angular reste actif à ce stade,
 * comme pour `inject()`, donc `effect()` fonctionne normalement ici. */
export function syncPayloadFromJson<T>(payloadJson: Signal<string>, defaultPayload: () => T, transform?: (parsed: T) => T): WritableSignal<T> {
  const payload = signal<T>(defaultPayload());
  effect(() => {
    const parsed = parsePayloadJson(payloadJson(), defaultPayload);
    payload.set(transform ? transform(parsed) : parsed);
  });
  return payload;
}

function parsePayloadJson<T>(json: string, defaultPayload: () => T): T {
  try {
    return { ...defaultPayload(), ...JSON.parse(json) };
  } catch {
    return defaultPayload();
  }
}

/** Mode de points par défaut de la manche (round-config), lu depuis configJson. */
export function roundPointsModeFrom(configJson: string): PointsMode {
  try {
    const parsed = JSON.parse(configJson);
    return parsed.pointsMode === 'PerAnswer' ? 'PerAnswer' : 'Uniform';
  } catch {
    return 'Uniform';
  }
}

/** ExpectedAnswers si renseigné, sinon reconstruit depuis l'ancien acceptedAnswers plat (une seule
 * réponse attendue, synonymes = l'ancienne liste) — miroir de QaQuestionPayload.ExpectedAnswersOrLegacy()
 * côté backend, pour que l'éditeur affiche correctement les questions créées avant les réponses multiples. */
export function toExpectedAnswers(payload: { expectedAnswers: ExpectedAnswerDraft[]; acceptedAnswers: string[] }): ExpectedAnswerDraft[] {
  if (payload.expectedAnswers.length > 0) {
    return payload.expectedAnswers;
  }
  if (payload.acceptedAnswers.length > 0) {
    return [{ acceptedVariants: payload.acceptedAnswers, points: null }];
  }
  return [];
}
