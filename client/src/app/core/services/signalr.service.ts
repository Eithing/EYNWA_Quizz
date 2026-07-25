import { Injectable } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { environment } from '../../../environments/environment';
import { Player, SessionState } from '../../models/session.model';

@Injectable({ providedIn: 'root' })
export class SignalrService {
  private connection: signalR.HubConnection | null = null;

  async connect(sessionCode: string): Promise<void> {
    const hubUrl = `${environment.apiBaseUrl.replace(/\/api$/, '')}/hubs/quiz`;

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl)
      .withAutomaticReconnect()
      .build();

    await this.connection.start();
    await this.connection.invoke('JoinSession', sessionCode);
  }

  onStepChanged(callback: (state: SessionState) => void): void {
    this.connection?.on('StepChanged', callback);
  }

  onPlayerJoined(callback: (player: Player) => void): void {
    this.connection?.on('PlayerJoined', callback);
  }

  onScoreUpdated(callback: (player: Player) => void): void {
    this.connection?.on('ScoreUpdated', callback);
  }

  async disconnect(): Promise<void> {
    await this.connection?.stop();
    this.connection = null;
  }
}
