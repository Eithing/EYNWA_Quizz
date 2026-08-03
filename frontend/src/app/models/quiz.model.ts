export interface Question {
  id?: number;
  order: number;
  payloadJson: string;
}

export interface Round {
  id?: number;
  order: number;
  featureTypeKey: string;
  title: string;
  configJson: string;
  restrictsParticipants: boolean;
  questions: Question[];
  /** Manche "à thèmes" : ne porte pas de questions directement, contient des sous-manches à la place. */
  isThemePicker: boolean;
  subRounds: Round[];
}

export interface QuizSummary {
  id: number;
  title: string;
  description?: string | null;
  updatedAt: string;
  roundCount: number;
}

export interface QuizDetail {
  id: number;
  title: string;
  description?: string | null;
  createdAt: string;
  updatedAt: string;
  rounds: Round[];
}

export interface SaveQuizRequest {
  title: string;
  description?: string | null;
  rounds: Round[];
}
