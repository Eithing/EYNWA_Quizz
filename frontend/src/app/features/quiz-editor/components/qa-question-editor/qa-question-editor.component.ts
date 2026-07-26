import { Component, effect, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

interface QaQuestionPayload {
  questionText: string;
  acceptedAnswers: string[];
}

function defaultPayload(): QaQuestionPayload {
  return { questionText: '', acceptedAnswers: [] };
}

@Component({
  selector: 'app-qa-question-editor',
  imports: [FormsModule],
  templateUrl: './qa-question-editor.component.html',
  styleUrl: './qa-question-editor.component.scss'
})
export class QaQuestionEditorComponent {
  readonly payloadJson = input.required<string>();
  readonly payloadJsonChange = output<string>();

  protected readonly payload = signal<QaQuestionPayload>(defaultPayload());
  protected readonly acceptedAnswersText = signal('');

  constructor() {
    effect(() => {
      const parsed = this.parse(this.payloadJson());
      this.payload.set(parsed);
      this.acceptedAnswersText.set(parsed.acceptedAnswers.join(', '));
    });
  }

  private parse(json: string): QaQuestionPayload {
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

  protected onAcceptedAnswersChange(value: string): void {
    this.acceptedAnswersText.set(value);
    const answers = value
      .split(',')
      .map((a) => a.trim())
      .filter((a) => a.length > 0);

    this.payload.update((p) => ({ ...p, acceptedAnswers: answers }));
    this.emit();
  }
}
