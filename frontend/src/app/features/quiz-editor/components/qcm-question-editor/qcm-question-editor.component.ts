import { Component, computed, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { QcmOptionDraft, QcmOptionsEditorComponent } from '../qcm-options-editor/qcm-options-editor.component';
import { PointsMode, roundPointsModeFrom, syncPayloadFromJson } from '../question-editor-payload.util';

interface QcmQuestionPayload {
  questionText: string;
  options: QcmOptionDraft[];
  /** Surcharge du mode de points de la manche pour CETTE question. Null = suit le réglage de la manche. */
  pointsModeOverride: 'Uniform' | 'PerAnswer' | null;
}

function defaultPayload(): QcmQuestionPayload {
  return { questionText: '', options: [], pointsModeOverride: null };
}

@Component({
  selector: 'app-qcm-question-editor',
  imports: [FormsModule, QcmOptionsEditorComponent],
  templateUrl: './qcm-question-editor.component.html',
  styleUrl: './qcm-question-editor.component.scss'
})
export class QcmQuestionEditorComponent {
  readonly payloadJson = input.required<string>();
  readonly configJson = input<string>('{}');
  readonly payloadJsonChange = output<string>();

  protected readonly payload = syncPayloadFromJson(this.payloadJson, defaultPayload);

  /** Réglage par défaut de la manche (round-config), avant surcharge éventuelle par cette question. */
  protected readonly roundPointsMode = computed<PointsMode>(() => roundPointsModeFrom(this.configJson()));

  /** Mode réellement appliqué à cette question : sa propre surcharge si renseignée, sinon celui de la manche. */
  protected readonly effectivePointsMode = computed<PointsMode>(() => this.payload().pointsModeOverride ?? this.roundPointsMode());

  private emit(): void {
    this.payloadJsonChange.emit(JSON.stringify(this.payload()));
  }

  protected onQuestionTextChange(value: string): void {
    this.payload.update((p) => ({ ...p, questionText: value }));
    this.emit();
  }

  protected onOptionsChange(options: QcmOptionDraft[]): void {
    this.payload.update((p) => ({ ...p, options }));
    this.emit();
  }

  protected onPointsModeOverrideChange(value: string): void {
    this.payload.update((p) => ({ ...p, pointsModeOverride: value === '' ? null : (value as 'Uniform' | 'PerAnswer') }));
    this.emit();
  }
}
