import { Component, computed, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { FeatureMeta } from '../../../../models/feature.model';
import { QuestionDraft, RoundDraft, toQuestionDraft, toRoundDraft } from '../../models/round-draft.model';
import { BlindTestQuestionEditorComponent } from '../blind-test-question-editor/blind-test-question-editor.component';
import { ClosestGuessQuestionEditorComponent } from '../closest-guess-question-editor/closest-guess-question-editor.component';
import { ClosestGuessRoundConfigComponent } from '../closest-guess-round-config/closest-guess-round-config.component';
import { FeaturePickerComponent } from '../feature-picker/feature-picker.component';
import { ImageGuessQuestionEditorComponent } from '../image-guess-question-editor/image-guess-question-editor.component';
import { OrderListQuestionEditorComponent } from '../order-list-question-editor/order-list-question-editor.component';
import { OrderListRoundConfigComponent } from '../order-list-round-config/order-list-round-config.component';
import { PartnerGuessQuestionEditorComponent } from '../partner-guess-question-editor/partner-guess-question-editor.component';
import { QaQuestionEditorComponent } from '../qa-question-editor/qa-question-editor.component';
import { QaRoundConfigComponent } from '../qa-round-config/qa-round-config.component';
import { QcmQuestionEditorComponent } from '../qcm-question-editor/qcm-question-editor.component';
import { QcmRoundConfigComponent } from '../qcm-round-config/qcm-round-config.component';
import { ZoomQuestionEditorComponent } from '../zoom-question-editor/zoom-question-editor.component';
import { ZoomRoundConfigComponent } from '../zoom-round-config/zoom-round-config.component';

@Component({
  selector: 'app-round-editor',
  imports: [
    FormsModule,
    ZoomRoundConfigComponent,
    ZoomQuestionEditorComponent,
    QaRoundConfigComponent,
    QaQuestionEditorComponent,
    BlindTestQuestionEditorComponent,
    ImageGuessQuestionEditorComponent,
    ClosestGuessQuestionEditorComponent,
    ClosestGuessRoundConfigComponent,
    PartnerGuessQuestionEditorComponent,
    OrderListRoundConfigComponent,
    OrderListQuestionEditorComponent,
    QcmRoundConfigComponent,
    QcmQuestionEditorComponent,
    FeaturePickerComponent,
    RoundEditorComponent
  ],
  templateUrl: './round-editor.component.html',
  styleUrl: './round-editor.component.scss'
})
export class RoundEditorComponent {
  readonly round = input.required<RoundDraft>();
  readonly roundChange = output<RoundDraft>();
  /** Vrai quand ce composant édite un thème (sous-manche) plutôt qu'une manche de premier niveau — masque
   * les options qui n'ont pas de sens à ce niveau (restriction de participants, manche à thèmes imbriquée). */
  readonly isNested = input(false);

  protected readonly isZoomImage = computed(() => this.round().featureTypeKey === 'zoom-image');
  protected readonly isQaText = computed(() => this.round().featureTypeKey === 'qa-text');
  protected readonly isBlindTest = computed(() => this.round().featureTypeKey === 'blind-test');
  protected readonly isImageGuess = computed(() => this.round().featureTypeKey === 'image-guess');
  protected readonly isClosestGuess = computed(() => this.round().featureTypeKey === 'closest-guess');
  protected readonly isPartnerGuess = computed(() => this.round().featureTypeKey === 'partner-guess');
  protected readonly isOrderList = computed(() => this.round().featureTypeKey === 'order-list');
  protected readonly isQcm = computed(() => this.round().featureTypeKey === 'multiple-choice');
  // qa-text, blind-test, image-guess et partner-guess partagent exactement la même configuration de manche.
  protected readonly usesQaRoundConfig = computed(
    () => this.isQaText() || this.isBlindTest() || this.isImageGuess() || this.isPartnerGuess()
  );

  protected readonly addingTheme = signal(false);
  protected readonly selectedSubRoundClientId = signal<number | null>(null);

  protected readonly selectedSubRound = computed(
    () => this.round().subRounds.find((r) => r.clientId === this.selectedSubRoundClientId()) ?? null
  );

  protected onTitleChange(title: string): void {
    this.roundChange.emit({ ...this.round(), title });
  }

  protected onRestrictsParticipantsChange(restrictsParticipants: boolean): void {
    this.roundChange.emit({ ...this.round(), restrictsParticipants });
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

  // --- Sous-manches (manche à thèmes) ---

  protected selectSubRound(clientId: number): void {
    this.selectedSubRoundClientId.set(clientId);
    this.addingTheme.set(false);
  }

  protected startAddTheme(): void {
    this.addingTheme.set(true);
    this.selectedSubRoundClientId.set(null);
  }

  protected cancelAddTheme(): void {
    this.addingTheme.set(false);
  }

  protected onThemeFeatureSelected(feature: FeatureMeta): void {
    const draft = toRoundDraft({
      order: this.round().subRounds.length,
      featureTypeKey: feature.typeKey,
      title: `Thème — ${feature.displayName}`,
      configJson: '{}',
      restrictsParticipants: false,
      isThemePicker: false,
      questions: [],
      subRounds: []
    });

    this.roundChange.emit({ ...this.round(), subRounds: [...this.round().subRounds, draft] });
    this.addingTheme.set(false);
    this.selectedSubRoundClientId.set(draft.clientId);
  }

  protected removeSubRound(clientId: number): void {
    const subRounds = this.round()
      .subRounds.filter((r) => r.clientId !== clientId)
      .map((r, i) => ({ ...r, order: i }));
    this.roundChange.emit({ ...this.round(), subRounds });
    if (this.selectedSubRoundClientId() === clientId) {
      this.selectedSubRoundClientId.set(null);
    }
  }

  protected onSubRoundChange(updated: RoundDraft): void {
    const subRounds = this.round().subRounds.map((r) => (r.clientId === updated.clientId ? updated : r));
    this.roundChange.emit({ ...this.round(), subRounds });
  }

  protected trackSubRound(_: number, round: RoundDraft): number {
    return round.clientId;
  }
}
