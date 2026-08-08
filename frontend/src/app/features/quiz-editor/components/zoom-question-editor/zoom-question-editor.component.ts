import { Component, computed, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MediaService } from '../../../../core/services/media.service';
import { MediaUploadFieldComponent } from '../../../../shared/components/media-upload-field/media-upload-field.component';
import { ExpectedAnswerDraft, ExpectedAnswersEditorComponent } from '../expected-answers-editor/expected-answers-editor.component';
import { PointsMode, roundPointsModeFrom, syncPayloadFromJson, toExpectedAnswers } from '../question-editor-payload.util';

interface ZoomQuestionPayload {
  imageUrl: string;
  /** Legacy, lu uniquement pour la rétrocompatibilité — voir toExpectedAnswers(). */
  acceptedAnswers: string[];
  expectedAnswers: ExpectedAnswerDraft[];
  zoomFocusPoint: { x: number; y: number };
  /** Surcharge du mode de points de la manche pour CETTE question. Null = suit le réglage de la manche. */
  pointsMode: 'Uniform' | 'PerAnswer' | null;
  /** Commentaire/consigne optionnel affiché à côté de l'image (ex: "Donnez le nom de l'objet et du jeu"). */
  comment: string;
}

function defaultPayload(): ZoomQuestionPayload {
  return { imageUrl: '', acceptedAnswers: [], expectedAnswers: [], zoomFocusPoint: { x: 0.5, y: 0.5 }, pointsMode: null, comment: '' };
}

@Component({
  selector: 'app-zoom-question-editor',
  imports: [FormsModule, ExpectedAnswersEditorComponent, MediaUploadFieldComponent],
  templateUrl: './zoom-question-editor.component.html',
  styleUrl: './zoom-question-editor.component.scss'
})
export class ZoomQuestionEditorComponent {
  readonly payloadJson = input.required<string>();
  readonly configJson = input<string>('{}');
  readonly payloadJsonChange = output<string>();

  protected readonly payload = syncPayloadFromJson(this.payloadJson, defaultPayload, (parsed) => ({
    ...parsed,
    expectedAnswers: toExpectedAnswers(parsed)
  }));

  /** Réglage par défaut de la manche (round-config), avant surcharge éventuelle par cette question. */
  protected readonly roundPointsMode = computed<PointsMode>(() => roundPointsModeFrom(this.configJson()));

  /** Mode réellement appliqué à cette question : sa propre surcharge si renseignée, sinon celui de la manche. */
  protected readonly effectivePointsMode = computed<PointsMode>(() => this.payload().pointsMode ?? this.roundPointsMode());

  constructor(protected readonly mediaService: MediaService) {}

  private emit(): void {
    this.payloadJsonChange.emit(JSON.stringify(this.payload()));
  }

  protected onImageUploaded(url: string): void {
    this.payload.update((p) => ({ ...p, imageUrl: url }));
    this.emit();
  }

  protected onCommentChange(value: string): void {
    this.payload.update((p) => ({ ...p, comment: value }));
    this.emit();
  }

  protected onImageClick(event: MouseEvent): void {
    const target = event.currentTarget as HTMLElement;
    const rect = target.getBoundingClientRect();
    const x = (event.clientX - rect.left) / rect.width;
    const y = (event.clientY - rect.top) / rect.height;

    this.payload.update((p) => ({ ...p, zoomFocusPoint: { x, y } }));
    this.emit();
  }

  protected onExpectedAnswersChange(expectedAnswers: ExpectedAnswerDraft[]): void {
    this.payload.update((p) => ({ ...p, expectedAnswers, acceptedAnswers: [] }));
    this.emit();
  }

  protected onPointsModeOverrideChange(value: string): void {
    this.payload.update((p) => ({ ...p, pointsMode: value === '' ? null : (value as 'Uniform' | 'PerAnswer') }));
    this.emit();
  }

  protected resolveImageUrl(url: string): string {
    return this.mediaService.resolveUrl(url);
  }
}
