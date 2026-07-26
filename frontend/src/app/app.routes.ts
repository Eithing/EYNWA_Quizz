import { Routes } from '@angular/router';
import { authGuard, guestGuard } from './core/guards/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/login/login.component').then((m) => m.LoginComponent)
  },
  {
    path: 'auth/callback',
    loadComponent: () =>
      import('./features/auth/callback/auth-callback.component').then((m) => m.AuthCallbackComponent)
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./features/library/library.component').then((m) => m.LibraryComponent)
  },
  {
    path: 'quizzes/:id',
    canActivate: [authGuard],
    loadComponent: () => import('./features/quiz-editor/quiz-editor.component').then((m) => m.QuizEditorComponent)
  },
  {
    path: 'host/:sessionId',
    canActivate: [authGuard],
    loadComponent: () => import('./features/host-live/host-live.component').then((m) => m.HostLiveComponent)
  },
  {
    path: 'join/:token',
    loadComponent: () => import('./features/join/join.component').then((m) => m.JoinComponent)
  },
  {
    path: 'play/:token',
    loadComponent: () => import('./features/play/play.component').then((m) => m.PlayComponent)
  }
];
