import { ZoomLevel } from '../../../models/quiz-config.model';

/** Vue "joueur" du config JSON d'une étape : les champs réponse sont déjà retirés côté serveur. */
export interface PlayerConfigView {
  mediaAssetId?: number;
  coordinates?: { x: number; y: number };
  zoomLevels?: ZoomLevel[];
  timePerLevelSec?: number;
  listenDurationSec?: number;
  question?: string;
}

export function parsePlayerConfig(configJson: string): PlayerConfigView {
  try {
    return JSON.parse(configJson);
  } catch {
    return {};
  }
}
