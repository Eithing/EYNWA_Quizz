import { Component, effect, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

interface ClosestGuessQuestionPayload {
  questionText: string;
  targetValue: number;
}

function defaultPayload(): ClosestGuessQuestionPayload {
  return { questionText: '', targetValue: 0 };
}

@Component({
  selector: 'app-closest-guess-question-editor',
  imports: [FormsModule],
  templateUrl: './closest-guess-question-editor.component.html',
  styleUrl: './closest-guess-question-editor.component.scss'
})
export class ClosestGuessQuestionEditorComponent {
  readonly payloadJson = input.required<string>();
  readonly payloadJsonChange = output<string>();

  protected readonly payload = signal<ClosestGuessQuestionPayload>(defaultPayload());

  constructor() {
    effect(() => this.payload.set(this.parse(this.payloadJson())));
  }

  private parse(json: string): ClosestGuessQuestionPayload {
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

  protected onTargetValueChange(value: number): void {
    this.payload.update((p) => ({ ...p, targetValue: value }));
    this.emit();
  }
}
