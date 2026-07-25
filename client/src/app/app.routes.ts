import { Routes } from '@angular/router';
import { authGuard, guestGuard } from './core/guards/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/login/login.component').then((m) => m.LoginComponent)
  },
  {
    path: 'register',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/register/register.component').then((m) => m.RegisterComponent)
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./features/dashboard/dashboard.component').then((m) => m.DashboardComponent)
  },
  {
    path: 'quizzes/:id',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/quiz-builder/quiz-builder.component').then((m) => m.QuizBuilderComponent)
  },
  {
    path: 'host/:sessionId',
    canActivate: [authGuard],
    loadComponent: () => import('./features/host/host.component').then((m) => m.HostComponent)
  },
  {
    path: 'join/:code',
    loadComponent: () => import('./features/join/join.component').then((m) => m.JoinComponent)
  },
  {
    path: 'play/:code',
    loadComponent: () => import('./features/play/play.component').then((m) => m.PlayComponent)
  }
];
