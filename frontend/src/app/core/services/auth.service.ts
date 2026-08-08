import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, signal } from '@angular/core';
import { Router } from '@angular/router';
import { environment } from '../../../environments/environment';
import { CurrentUser } from '../../models/current-user.model';

const TOKEN_STORAGE_KEY = 'quizparty_token';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly tokenSignal = signal<string | null>(localStorage.getItem(TOKEN_STORAGE_KEY));
  private readonly currentUserSignal = signal<CurrentUser | null>(null);

  readonly token = this.tokenSignal.asReadonly();
  readonly currentUser = this.currentUserSignal.asReadonly();
  readonly isAuthenticated = computed(() => this.tokenSignal() !== null);

  /** Navigation complète (pas un appel XHR) : déclenche le challenge OAuth Discord côté backend. */
  readonly discordLoginUrl = `${environment.apiBaseUrl}/auth/discord/login`;

  constructor(
    private readonly http: HttpClient,
    private readonly router: Router
  ) {
    if (this.tokenSignal()) {
      this.fetchCurrentUser();
    }
  }

  setToken(token: string): void {
    localStorage.setItem(TOKEN_STORAGE_KEY, token);
    this.tokenSignal.set(token);
    this.fetchCurrentUser();
  }

  fetchCurrentUser(): void {
    this.http.get<CurrentUser>(`${environment.apiBaseUrl}/api/auth/me`).subscribe({
      next: (user) => this.currentUserSignal.set(user),
      // Ne déconnecter que sur un vrai 401 (token invalide/expiré) : un hoquet réseau ou un backend
      // momentanément indisponible (ex. redémarrage, "no healthy upstream" derrière Cloudflare) ne doit
      // pas faire perdre la session d'un GM dont le JWT reste par ailleurs valide.
      error: (err: unknown) => {
        if (err instanceof HttpErrorResponse && err.status === 401) {
          this.logout();
        }
      }
    });
  }

  logout(): void {
    localStorage.removeItem(TOKEN_STORAGE_KEY);
    this.tokenSignal.set(null);
    this.currentUserSignal.set(null);
    this.router.navigateByUrl('/login');
  }
}
