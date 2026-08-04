import { Component, OnDestroy, OnInit, computed, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { GameSignalrService } from '../../core/services/game-signalr.service';
import { MediaService } from '../../core/services/media.service';
import { SessionService } from '../../core/services/session.service';
import { AnswerFeedItem, CurrentQuestionAdmin, GameSessionState, Player, RoundPreview, Team } from '../../models/session.model';
import { AudioPlayerComponent } from '../../shared/components/audio-player/audio-player.component';
import { ParticipantSelection, ParticipantSelectorComponent } from '../../shared/components/participant-selector/participant-selector.component';
import { UiCardComponent } from '../../shared/components/ui-card/ui-card.component';

const POLL_INTERVAL_MS = 1000;

interface TeamDraft {
  name: string;
  playerIds: Set<number>;
}

@Component({
  selector: 'app-host-live',
  imports: [UiCardComponent, AudioPlayerComponent, ParticipantSelectorComponent],
  templateUrl: './host-live.component.html',
  styleUrl: './host-live.component.scss'
})
export class HostLiveComponent implements OnInit, OnDestroy {
  private sessionId!: number;
  private pollHandle?: ReturnType<typeof setInterval>;

  protected readonly state = signal<GameSessionState | null>(null);
  protected readonly currentQuestion = signal<CurrentQuestionAdmin | null>(null);
  protected readonly answerFeed = signal<AnswerFeedItem[]>([]);
  protected readonly pendingRoundPreview = signal<RoundPreview | null>(null);
  protected readonly copied = signal(false);
  protected readonly adjustingPlayerId = signal<number | null>(null);
  protected readonly adjustDelta = signal(0);
  protected readonly adjustReason = signal('');

  protected readonly adjustingTeamId = signal<number | null>(null);
  protected readonly teamAdjustDelta = signal(0);
  protected readonly teamAdjustReason = signal('');

  protected readonly teamBuilderOpen = signal(false);
  protected readonly teamDrafts = signal<TeamDraft[]>([]);

  protected readonly themeSelectorOpenId = signal<number | null>(null);
  protected readonly partnerGuessSelectorOpen = signal(false);

  protected readonly pendingRoundImageUrl = computed(() => {
    const preview = this.pendingRoundPreview();
    if (!preview || preview.featureTypeKey !== 'zoom-image' || !preview.firstQuestionPayloadJson) {
      return null;
    }
    try {
      const imageUrl = JSON.parse(preview.firstQuestionPayloadJson).imageUrl as string;
      return imageUrl ? this.mediaService.resolveUrl(imageUrl) : null;
    } catch {
      return null;
    }
  });

  protected readonly pendingRoundQaText = computed(() => {
    const preview = this.pendingRoundPreview();
    if (!preview || preview.featureTypeKey !== 'qa-text' || !preview.firstQuestionPayloadJson) {
      return null;
    }
    try {
      return (JSON.parse(preview.firstQuestionPayloadJson) as { questionText: string }).questionText;
    } catch {
      return null;
    }
  });

  protected readonly pendingRoundAudioUrl = computed(() => {
    const preview = this.pendingRoundPreview();
    if (!preview || preview.featureTypeKey !== 'blind-test' || !preview.firstQuestionPayloadJson) {
      return null;
    }
    try {
      const audioUrl = JSON.parse(preview.firstQuestionPayloadJson).audioUrl as string;
      return audioUrl ? this.mediaService.resolveUrl(audioUrl) : null;
    } catch {
      return null;
    }
  });

  protected readonly pendingRoundImageGuessUrl = computed(() => {
    const preview = this.pendingRoundPreview();
    if (!preview || preview.featureTypeKey !== 'image-guess' || !preview.firstQuestionPayloadJson) {
      return null;
    }
    try {
      const imageUrl = JSON.parse(preview.firstQuestionPayloadJson).imageUrl as string;
      return imageUrl ? this.mediaService.resolveUrl(imageUrl) : null;
    } catch {
      return null;
    }
  });

  protected readonly currentImageUrl = computed(() => {
    const question = this.currentQuestion();
    if (!question || question.featureTypeKey !== 'zoom-image') {
      return null;
    }
    try {
      const imageUrl = JSON.parse(question.payloadJson).imageUrl as string;
      return imageUrl ? this.mediaService.resolveUrl(imageUrl) : null;
    } catch {
      return null;
    }
  });

  protected readonly currentQaPayload = computed(() => {
    const question = this.currentQuestion();
    if (!question || question.featureTypeKey !== 'qa-text') {
      return null;
    }
    try {
      return JSON.parse(question.payloadJson) as { questionText: string; acceptedAnswers: string[] };
    } catch {
      return null;
    }
  });

  protected readonly currentBlindTestPayload = computed(() => {
    const question = this.currentQuestion();
    if (!question || question.featureTypeKey !== 'blind-test') {
      return null;
    }
    try {
      const parsed = JSON.parse(question.payloadJson) as { audioUrl: string; acceptedAnswers: string[] };
      return { ...parsed, resolvedAudioUrl: this.mediaService.resolveUrl(parsed.audioUrl) };
    } catch {
      return null;
    }
  });

  protected readonly currentImageGuessPayload = computed(() => {
    const question = this.currentQuestion();
    if (!question || question.featureTypeKey !== 'image-guess') {
      return null;
    }
    try {
      const parsed = JSON.parse(question.payloadJson) as { imageUrl: string; acceptedAnswers: string[] };
      return { ...parsed, resolvedImageUrl: this.mediaService.resolveUrl(parsed.imageUrl) };
    } catch {
      return null;
    }
  });

  protected readonly currentClosestGuessPayload = computed(() => {
    const question = this.currentQuestion();
    if (!question || question.featureTypeKey !== 'closest-guess') {
      return null;
    }
    try {
      return JSON.parse(question.payloadJson) as { questionText: string; targetValue: number };
    } catch {
      return null;
    }
  });

  protected readonly anyThemeHidden = computed(() => (this.state()?.themeBoard ?? []).some((t) => !t.isRevealed));

  protected readonly allThemesResolved = computed(() => {
    const board = this.state()?.themeBoard ?? [];
    return board.length > 0 && board.every((t) => t.resolution !== 'Pending');
  });

  protected readonly participantBanner = computed(() => {
    const s = this.state();
    if (!s) {
      return null;
    }
    if (s.currentRoundParticipantTeamIds.length > 0) {
      const names = s.teams.filter((t) => s.currentRoundParticipantTeamIds.includes(t.id)).map((t) => t.name);
      return names.length > 0 ? `Manche restreinte : ${names.join(', ')} — les autres sont spectateurs.` : null;
    }
    if (s.currentRoundParticipantPlayerIds.length > 0) {
      const names = s.players.filter((p) => s.currentRoundParticipantPlayerIds.includes(p.id)).map((p) => p.pseudo);
      return names.length > 0 ? `Manche restreinte : ${names.join(', ')} — les autres sont spectateurs.` : null;
    }
    return null;
  });

  protected readonly currentPartnerGuessPayload = computed(() => {
    const question = this.currentQuestion();
    if (!question || question.featureTypeKey !== 'partner-guess') {
      return null;
    }
    try {
      return JSON.parse(question.payloadJson) as { questionText: string; acceptedAnswers: string[] };
    } catch {
      return null;
    }
  });

  /** Phase 1 encore en cours (le répondant n'a pas fini) : les participants désignés sont encore
   * uniquement le répondant lui-même — le GM peut à tout moment passer à la phase de devinette. */
  protected readonly isPartnerGuessPhase1 = computed(() => {
    const s = this.state();
    if (!s || s.status !== 'Running' || this.currentQuestion()?.featureTypeKey !== 'partner-guess') {
      return false;
    }
    return (
      s.currentAnswererPlayerId !== null &&
      s.currentRoundParticipantPlayerIds.length === 1 &&
      s.currentRoundParticipantPlayerIds[0] === s.currentAnswererPlayerId
    );
  });

  protected readonly partnerGuessGuesserPool = computed<{ players: Player[]; teams: Team[] }>(() => {
    const s = this.state();
    if (!s) {
      return { players: [], teams: [] };
    }
    return { players: s.players.filter((p) => p.id !== s.currentAnswererPlayerId), teams: s.teams };
  });

  constructor(
    private readonly route: ActivatedRoute,
    private readonly sessionService: SessionService,
    private readonly signalrService: GameSignalrService,
    private readonly mediaService: MediaService
  ) {}

  ngOnInit(): void {
    this.sessionId = Number(this.route.snapshot.paramMap.get('sessionId'));

    this.sessionService.getStateAsGm(this.sessionId).subscribe(async (state) => {
      this.state.set(state);
      this.refreshCurrentQuestion();
      this.refreshAnswerFeed();

      await this.signalrService.connect(state.inviteToken);
      this.signalrService.onStateChanged((updated) => {
        this.state.set(updated);
        this.refreshCurrentQuestion();
      });
      this.signalrService.onPlayerJoined((player) => {
        this.state.update((s) => (s ? { ...s, players: [...s.players, player] } : s));
      });
      this.signalrService.onScoreUpdated((player) => {
        this.state.update((s) =>
          s
            ? {
                ...s,
                players: s.players.map((p) => (p.id === player.id ? player : p)).sort((a, b) => b.totalScore - a.totalScore)
              }
            : s
        );
        this.refreshAnswerFeed();
      });
      this.signalrService.onAnswerPendingValidation(() => this.refreshAnswerFeed());
    });

    // Filet de sécurité indépendant de SignalR : si un message temps réel est manqué, l'écran
    // se remet à jour tout seul au prochain sondage (nouveaux joueurs, scores, question courante).
    this.pollHandle = setInterval(() => this.refreshState(), POLL_INTERVAL_MS);
  }

  ngOnDestroy(): void {
    clearInterval(this.pollHandle);
    this.signalrService.disconnect();
  }

  private refreshState(): void {
    this.sessionService.getStateAsGm(this.sessionId).subscribe((state) => {
      this.state.set(state);
      this.refreshCurrentQuestion();
      this.refreshAnswerFeed();
    });
  }

  protected teamName(teamId: number | null): string {
    return this.state()?.teams.find((t) => t.id === teamId)?.name ?? '';
  }

  protected get inviteUrl(): string {
    const token = this.state()?.inviteToken;
    return token ? `${window.location.origin}/join/${token}` : '';
  }

  protected copyInviteLink(): void {
    navigator.clipboard.writeText(this.inviteUrl).then(() => {
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 2000);
    });
  }

  protected begin(): void {
    this.sessionService.begin(this.sessionId).subscribe((state) => this.applyState(state));
  }

  protected pause(): void {
    this.sessionService.pause(this.sessionId).subscribe((state) => this.applyState(state));
  }

  protected resume(): void {
    this.sessionService.resume(this.sessionId).subscribe((state) => this.applyState(state));
  }

  protected next(): void {
    this.sessionService.next(this.sessionId).subscribe((state) => this.applyState(state));
  }

  protected toggleScoreboard(): void {
    const current = this.state()?.scoreboardVisible ?? false;
    this.sessionService.setScoreboardVisible(this.sessionId, !current).subscribe((state) => this.applyState(state));
  }

  protected toggleTeamScoring(): void {
    const current = this.state()?.teamScoringEnabled ?? false;
    this.sessionService.setTeamScoring(this.sessionId, !current).subscribe((state) => this.applyState(state));
  }

  protected onSetRoundParticipants(selection: ParticipantSelection): void {
    this.sessionService
      .setRoundParticipants(this.sessionId, selection.playerIds, selection.teamIds)
      .subscribe((state) => this.applyState(state));
  }

  protected onSetRoundTeamMode(enabled: boolean): void {
    this.sessionService.setRoundTeamMode(this.sessionId, enabled).subscribe((state) => this.applyState(state));
  }

  protected resolveBuzz(isCorrect: boolean): void {
    this.sessionService.resolveBuzz(this.sessionId, isCorrect).subscribe((state) => this.applyState(state));
  }

  protected setAnswerVerdict(answer: AnswerFeedItem, isCorrect: boolean): void {
    this.sessionService.validateAnswer(this.sessionId, answer.id, isCorrect).subscribe(() => {
      this.refreshAnswerFeed();
      this.refreshCurrentQuestion();
    });
  }

  private applyState(state: GameSessionState): void {
    this.state.set(state);
    this.refreshCurrentQuestion();
  }

  private refreshCurrentQuestion(): void {
    const state = this.state();

    if (state?.status === 'AwaitingParticipants' || state?.status === 'AwaitingTeamMode') {
      this.currentQuestion.set(null);
      this.sessionService.getPendingRoundPreview(this.sessionId).subscribe({
        next: (preview) => this.pendingRoundPreview.set(preview),
        error: () => this.pendingRoundPreview.set(null)
      });
      return;
    }

    this.pendingRoundPreview.set(null);

    if (!state || state.status !== 'Running') {
      this.currentQuestion.set(null);
      return;
    }

    this.sessionService.getCurrentQuestionFull(this.sessionId).subscribe({
      next: (question) => this.currentQuestion.set(question),
      error: () => this.currentQuestion.set(null)
    });
  }

  protected startAdjustScore(playerId: number): void {
    this.adjustingPlayerId.set(playerId);
    this.adjustDelta.set(0);
    this.adjustReason.set('');
  }

  protected cancelAdjustScore(): void {
    this.adjustingPlayerId.set(null);
  }

  protected confirmAdjustScore(): void {
    const playerId = this.adjustingPlayerId();
    const delta = this.adjustDelta();
    if (playerId === null || delta === 0) {
      return;
    }

    this.sessionService.adjustScore(this.sessionId, playerId, delta, this.adjustReason() || 'Ajustement manuel').subscribe(() => {
      this.adjustingPlayerId.set(null);
      this.refreshState();
    });
  }

  protected startAdjustTeamScore(teamId: number): void {
    this.adjustingTeamId.set(teamId);
    this.teamAdjustDelta.set(0);
    this.teamAdjustReason.set('');
  }

  protected cancelAdjustTeamScore(): void {
    this.adjustingTeamId.set(null);
  }

  protected confirmAdjustTeamScore(): void {
    const teamId = this.adjustingTeamId();
    const delta = this.teamAdjustDelta();
    if (teamId === null || delta === 0) {
      return;
    }

    this.sessionService.adjustTeamScore(this.sessionId, teamId, delta, this.teamAdjustReason() || 'Ajustement manuel').subscribe(() => {
      this.adjustingTeamId.set(null);
      this.refreshState();
    });
  }

  protected openTeamBuilder(): void {
    const existing = this.state()?.teams ?? [];
    this.teamDrafts.set(
      existing.length > 0
        ? existing.map((t) => ({ name: t.name, playerIds: new Set(t.playerIds) }))
        : [{ name: '', playerIds: new Set() }]
    );
    this.teamBuilderOpen.set(true);
  }

  protected cancelTeamBuilder(): void {
    this.teamBuilderOpen.set(false);
  }

  protected addTeamDraft(): void {
    this.teamDrafts.update((drafts) => [...drafts, { name: '', playerIds: new Set() }]);
  }

  protected removeTeamDraft(index: number): void {
    this.teamDrafts.update((drafts) => drafts.filter((_, i) => i !== index));
  }

  protected renameTeamDraft(index: number, name: string): void {
    this.teamDrafts.update((drafts) => drafts.map((d, i) => (i === index ? { ...d, name } : d)));
  }

  protected toggleDraftPlayer(index: number, playerId: number): void {
    this.teamDrafts.update((drafts) =>
      drafts.map((d, i) => {
        if (i !== index) {
          return d;
        }
        const playerIds = new Set(d.playerIds);
        playerIds.has(playerId) ? playerIds.delete(playerId) : playerIds.add(playerId);
        return { ...d, playerIds };
      })
    );
  }

  protected isPlayerTakenByOtherDraft(index: number, playerId: number): boolean {
    return this.teamDrafts().some((d, i) => i !== index && d.playerIds.has(playerId));
  }

  protected saveTeams(): void {
    const teams = this.teamDrafts()
      .filter((d) => d.name.trim().length > 0)
      .map((d) => ({ name: d.name.trim(), playerIds: [...d.playerIds] }));

    this.sessionService.setTeams(this.sessionId, teams).subscribe((state) => {
      this.applyState(state);
      this.teamBuilderOpen.set(false);
    });
  }

  protected toggleThemeSelector(subRoundId: number): void {
    this.themeSelectorOpenId.update((current) => (current === subRoundId ? null : subRoundId));
  }

  protected onChooseTheme(subRoundId: number, selection: ParticipantSelection): void {
    this.sessionService.chooseTheme(this.sessionId, subRoundId, selection.playerIds, selection.teamIds).subscribe((state) => {
      this.themeSelectorOpenId.set(null);
      this.applyState(state);
    });
  }

  protected skipTheme(subRoundId: number): void {
    this.sessionService.skipTheme(this.sessionId, subRoundId).subscribe((state) => this.applyState(state));
  }

  protected revealAllThemes(): void {
    this.sessionService.revealThemes(this.sessionId).subscribe((state) => this.applyState(state));
  }

  protected revealTheme(subRoundId: number): void {
    this.sessionService.revealThemes(this.sessionId, subRoundId).subscribe((state) => this.applyState(state));
  }

  protected revealDeferredScoring(): void {
    this.sessionService.revealDeferredScoring(this.sessionId).subscribe(() => this.refreshState());
  }

  protected setPartnerGuessAnswerer(playerId: number): void {
    this.sessionService.setPartnerGuessAnswerer(this.sessionId, playerId).subscribe((state) => this.applyState(state));
  }

  protected togglePartnerGuessSelector(): void {
    this.partnerGuessSelectorOpen.update((open) => !open);
  }

  protected startPartnerGuessGuessing(selection: ParticipantSelection): void {
    this.sessionService.startPartnerGuessGuessing(this.sessionId, selection.playerIds, selection.teamIds).subscribe((state) => {
      this.partnerGuessSelectorOpen.set(false);
      this.applyState(state);
    });
  }

  private refreshAnswerFeed(): void {
    this.sessionService.getCurrentQuestionAnswers(this.sessionId).subscribe((answers) => this.answerFeed.set(answers));
  }
}
