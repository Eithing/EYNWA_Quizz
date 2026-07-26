import { Component, computed, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { QuestionDraft, RoundDraft, toQuestionDraft } from '../../models/round-draft.model';
import { QaQuestionEditorComponent } from '../qa-question-editor/qa-question-editor.component';
import { QaRoundConfigComponent } from '../qa-round-config/qa-round-config.component';
import { ZoomQuestionEditorComponent } from '../zoom-question-editor/zoom-question-editor.component';
import { ZoomRoundConfigComponent } from '../zoom-round-config/zoom-round-config.component';

@Component({
  selector: 'app-round-editor',
  imports: [FormsModule, ZoomRoundConfigComponent, ZoomQuestionEditorComponent, QaRoundConfigComponent, QaQuestionEditorComponent],
  templateUrl: './round-editor.component.html',
  styleUrl: './round-editor.component.scss'
})
export class RoundEditorComponent {
  readonly round = input.required<RoundDraft>();
  readonly roundChange = output<RoundDraft>();

  protected readonly isZoomImage = computed(() => this.round().featureTypeKey === 'zoom-image');
  protected readonly isQaText = computed(() => this.round().featureTypeKey === 'qa-text');

  protected onTitleChange(title: string): void {
    this.roundChange.emit({ ...this.round(), title });
  }

  protected onRequiresTargetPlayerChange(requiresTargetPlayer: boolean): void {
    this.roundChange.emit({ ...this.round(), requiresTargetPlayer });
  }

  protected onConfigJsonChange(configJson: string): void {
    this.roundChange.emit({ ...this.round(), configJson });
  }

  protected onQuestionPayloadChange(clientId: number, payloadJson: string): void {
    this.roundChange.emit({
      ...this.round(),
      questions: this.round().questions.map((q) => (q.clientId === clientId ? { ...q, payloadJson } : q))
    });
  }

  protected addQuestion(): void {
    const questions = this.round().questions;
    const draft = toQuestionDraft({ order: questions.length, payloadJson: '{}' });
    this.roundChange.emit({ ...this.round(), questions: [...questions, draft] });
  }

  protected removeQuestion(clientId: number): void {
    const questions = this.round()
      .questions.filter((q) => q.clientId !== clientId)
      .map((q, i) => ({ ...q, order: i }));
    this.roundChange.emit({ ...this.round(), questions });
  }

  protected moveQuestion(clientId: number, offset: number): void {
    const questions = [...this.round().questions];
    const index = questions.findIndex((q) => q.clientId === clientId);
    const targetIndex = index + offset;
    if (index === -1 || targetIndex < 0 || targetIndex >= questions.length) {
      return;
    }

    [questions[index], questions[targetIndex]] = [questions[targetIndex], questions[index]];
    this.roundChange.emit({
      ...this.round(),
      questions: questions.map((q, i) => ({ ...q, order: i }))
    });
  }

  protected trackQuestion(_: number, question: QuestionDraft): number {
    return question.clientId;
  }
}
