import { Component, computed, effect, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  JokerType,
  ScoringType,
  StepConfigBase,
  TeamMode,
  ZoomLevel,
  defaultStepConfig
} from '../../../../models/quiz-config.model';
import { MediaAsset } from '../../../../models/media.model';
import { FileUploadComponent } from '../../../../shared/components/file-upload/file-upload.component';
import { QuizStepDraft } from '../../models/quiz-step-draft.model';

interface EditableConfig extends StepConfigBase {
  mediaAssetId?: number;
  coordinates?: { x: number; y: number };
  zoomLevels?: ZoomLevel[];
  timePerLevelSec?: number;
  listenDurationSec?: number;
  question?: string;
  answer?: string;
  toleranceRatio?: number;
}

const JOKER_OPTIONS: { value: JokerType; label: string }[] = [
  { value: 'CONVERT_TO_QCM', label: 'Conversion QCM' },
  { value: 'INDICE_VISUEL', label: 'Indice visuel' },
  { value: 'AIDE_BINOME', label: 'Aide du binôme (+10s)' }
];

@Component({
  selector: 'app-step-editor',
  imports: [FormsModule, FileUploadComponent],
  templateUrl: './step-editor.component.html',
  styleUrl: './step-editor.component.scss'
})
export class StepEditorComponent {
  readonly step = input.required<QuizStepDraft>();
  readonly stepChange = output<QuizStepDraft>();

  protected readonly jokerOptions = JOKER_OPTIONS;

  protected readonly title = signal('');
  protected readonly config = signal<EditableConfig>(defaultStepConfig());

  protected readonly isZoom = computed(() => this.step().type === 'ZoomProgressif');
  protected readonly isBlindTest = computed(() => this.step().type === 'BlindTest');
  protected readonly isQuestionDirecte = computed(() => this.step().type === 'QuestionDirecte');

  constructor() {
    effect(() => {
      const current = this.step();
      this.title.set(current.title);
      this.config.set(this.parseConfig(current.configJson));
    });
  }

  private parseConfig(configJson: string): EditableConfig {
    try {
      return { ...defaultStepConfig(), ...JSON.parse(configJson) };
    } catch {
      return defaultStepConfig();
    }
  }

  private emitChange(): void {
    this.stepChange.emit({
      ...this.step(),
      title: this.title(),
      configJson: JSON.stringify(this.config())
    });
  }

  protected onTitleChange(value: string): void {
    this.title.set(value);
    this.emitChange();
  }

  protected onModeChange(value: TeamMode): void {
    this.config.update((c) => ({ ...c, mode: value }));
    this.emitChange();
  }

  protected onScoringTypeChange(value: ScoringType): void {
    this.config.update((c) => ({ ...c, scoring: { ...c.scoring, type: value } }));
    this.emitChange();
  }

  protected onTempsParQuestionChange(value: number): void {
    this.config.update((c) => ({ ...c, triggers: { ...c.triggers, tempsParQuestionSec: value } }));
    this.emitChange();
  }

  protected toggleJoker(joker: JokerType): void {
    this.config.update((c) => {
      const has = c.jokersAllowed.includes(joker);
      return {
        ...c,
        jokersAllowed: has ? c.jokersAllowed.filter((j) => j !== joker) : [...c.jokersAllowed, joker]
      };
    });
    this.emitChange();
  }

  protected onCoordinateChange(axis: 'x' | 'y', value: number): void {
    this.config.update((c) => ({
      ...c,
      coordinates: { x: c.coordinates?.x ?? 0, y: c.coordinates?.y ?? 0, [axis]: value }
    }));
    this.emitChange();
  }

  protected onTimePerLevelChange(value: number): void {
    this.config.update((c) => ({ ...c, timePerLevelSec: value }));
    this.emitChange();
  }

  protected onZoomLevelChange(index: number, field: keyof ZoomLevel, value: number): void {
    this.config.update((c) => {
      const levels = [...(c.zoomLevels ?? [])];
      levels[index] = { ...levels[index], [field]: value };
      return { ...c, zoomLevels: levels };
    });
    this.emitChange();
  }

  protected addZoomLevel(): void {
    this.config.update((c) => ({ ...c, zoomLevels: [...(c.zoomLevels ?? []), { zoom: 100, pts: 1 }] }));
    this.emitChange();
  }

  protected removeZoomLevel(index: number): void {
    this.config.update((c) => ({ ...c, zoomLevels: (c.zoomLevels ?? []).filter((_, i) => i !== index) }));
    this.emitChange();
  }

  protected onListenDurationChange(value: number): void {
    this.config.update((c) => ({ ...c, listenDurationSec: value }));
    this.emitChange();
  }

  protected onQuestionChange(value: string): void {
    this.config.update((c) => ({ ...c, question: value }));
    this.emitChange();
  }

  protected onAnswerChange(value: string): void {
    this.config.update((c) => ({ ...c, answer: value }));
    this.emitChange();
  }

  protected onToleranceChange(value: number): void {
    this.config.update((c) => ({ ...c, toleranceRatio: value }));
    this.emitChange();
  }

  protected onMediaUploaded(asset: MediaAsset): void {
    this.config.update((c) => ({ ...c, mediaAssetId: asset.id }));
    this.emitChange();
  }
}
