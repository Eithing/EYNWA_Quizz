import { HttpClient } from '@angular/common/http';
import { Injectable, computed, signal } from '@angular/core';
import { Router } from '@angular/router';
import { environment } from '../../../environments/environment';
import { AuthCredentials, AuthResponse } from '../../models/auth.model';

const TOKEN_STORAGE_KEY = 'eynwa_quizz_token';
const USERNAME_STORAGE_KEY = 'eynwa_quizz_username';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly tokenSignal = signal<string | null>(localStorage.getItem(TOKEN_STORAGE_KEY));
  private readonly usernameSignal = signal<string | null>(localStorage.getItem(USERNAME_STORAGE_KEY));

  readonly token = this.tokenSignal.asReadonly();
  readonly username = this.usernameSignal.asReadonly();
  readonly isAuthenticated = computed(() => this.tokenSignal() !== null);

  constructor(
    private readonly http: HttpClient,
    private readonly router: Router
  ) {}

  register(credentials: AuthCredentials) {
    return this.http.post<AuthResponse>(`${environment.apiBaseUrl}/auth/register`, credentials);
  }

  login(credentials: AuthCredentials) {
    return this.http.post<AuthResponse>(`${environment.apiBaseUrl}/auth/login`, credentials);
  }

  setSession(response: AuthResponse): void {
    localStorage.setItem(TOKEN_STORAGE_KEY, response.token);
    localStorage.setItem(USERNAME_STORAGE_KEY, response.username);
    this.tokenSignal.set(response.token);
    this.usernameSignal.set(response.username);
  }

  logout(): void {
    localStorage.removeItem(TOKEN_STORAGE_KEY);
    localStorage.removeItem(USERNAME_STORAGE_KEY);
    this.tokenSignal.set(null);
    this.usernameSignal.set(null);
    this.router.navigateByUrl('/login');
  }
}
