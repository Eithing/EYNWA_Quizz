export type TeamMode = 'SOLO' | 'DUO' | 'TRIO' | 'CHEF_EQUIPE';

export type ScoringType = 'FIXE' | 'DEGRESSIF' | 'BONUS_VITESSE' | 'MALUS';

export type JokerType = 'CONVERT_TO_QCM' | 'INDICE_VISUEL' | 'AIDE_BINOME';

export type StepType =
  | 'ZoomProgressif'
  | 'BlindTest'
  | 'GeoGamer'
  | 'Memorisation'
  | 'DefileSuccessif'
  | 'QuestionDirecte'
  | 'TuPreferes'
  | 'ConnaissanceCroisee'
  | 'LePanel';

export interface StepTypeMeta {
  type: StepType;
  label: string;
  description: string;
  category: string;
}

export const STEP_TYPE_CATALOG: StepTypeMeta[] = [
  { type: 'ZoomProgressif', label: 'Zoom Progressif', description: "Dézoom progressif d'une image jusqu'à trouver la réponse.", category: 'Visuel' },
  { type: 'BlindTest', label: 'Blind Test Audio', description: 'Extrait sonore à deviner au buzzer.', category: 'Audio & Rapidité' },
  { type: 'GeoGamer', label: 'GeoGamer 360°', description: 'Vue immersive et pointage sur carte.', category: '3D / Immersif' },
  { type: 'Memorisation', label: 'Mémorisation', description: "Séquence d'éléments à mémoriser puis restituer.", category: 'Mémoire' },
  { type: 'DefileSuccessif', label: 'Défilé Successif', description: "Enchaînement rapide d'images à identifier.", category: 'Time Attack' },
  { type: 'QuestionDirecte', label: 'Question Directe', description: 'Question ouverte avec tolérance aux fautes.', category: 'Classique' },
  { type: 'TuPreferes', label: 'Tu Préfères', description: 'Les coéquipiers doivent donner la même réponse.', category: 'Coopération' },
  { type: 'ConnaissanceCroisee', label: 'Connaissance Croisée', description: "Deviner les réponses de son binôme.", category: 'Coopération' },
  { type: 'LePanel', label: 'Le Panel', description: "Lister un maximum d'éléments d'une catégorie.", category: 'Saisie de Masse' }
];

export interface Triggers {
  chronoGlobalSec?: number;
  tempsParQuestionSec?: number;
  nbEssais?: number;
}

export interface ScoringConfig {
  type: ScoringType;
  baremeParPalier?: number[];
  malusParErreur?: number;
}

export interface StepConfigBase {
  mode: TeamMode;
  scoring: ScoringConfig;
  triggers: Triggers;
  jokersAllowed: JokerType[];
}

export interface ZoomLevel {
  zoom: number;
  pts: number;
}

export interface ZoomProgressifConfig extends StepConfigBase {
  coordinates: { x: number; y: number };
  zoomLevels: ZoomLevel[];
  timePerLevelSec: number;
  mediaAssetId?: number;
}

export interface BlindTestConfig extends StepConfigBase {
  mediaAssetId?: number;
  listenDurationSec: number;
  answer: string;
}

export interface QuestionDirecteConfig extends StepConfigBase {
  question: string;
  answer: string;
  toleranceRatio: number;
}

export function defaultStepConfig(): StepConfigBase {
  return {
    mode: 'SOLO',
    scoring: { type: 'FIXE' },
    triggers: { tempsParQuestionSec: 30 },
    jokersAllowed: []
  };
}
