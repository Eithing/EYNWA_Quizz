import { Component, effect, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { QcmOptionDraft, QcmOptionsEditorComponent } from '../qcm-options-editor/qcm-options-editor.component';

interface QcmQuestionPayload {
  questionText: string;
  options: QcmOptionDraft[];
}

function defaultPayload(): QcmQuestionPayload {
  return { questionText: '', options: [] };
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

  protected readonly payload = signal<QcmQuestionPayload>(defaultPayload());

  constructor() {
    effect(() => this.payload.set(this.parse(this.payloadJson())));
  }

  private parse(json: string): QcmQuestionPayload {
    try {
      return { ...defaultPayload(), ...JSON.parse(json) };
    } catch {
      return defaultPayload();
    }
  }

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
}
