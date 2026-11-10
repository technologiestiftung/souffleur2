import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';
import { LoginComponent } from './features/login/login.component';
import { PlaceholderComponent } from './features/placeholder.component';
import { ShellComponent } from './shell/shell.component';

export const routes: Routes = [
  { path: 'anmelden', component: LoginComponent, title: 'Anmelden · souffleur' },
  {
    path: 'einladung',
    title: 'Einladung · souffleur',
    loadComponent: () =>
      import('./features/login/einladung.component').then((m) => m.EinladungComponent),
  },
  {
    path: '',
    component: ShellComponent,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'uebersicht' },
      {
        path: 'uebersicht',
        title: 'Übersicht · souffleur',
        loadComponent: () =>
          import('./features/dashboard/dashboard.component').then((m) => m.DashboardComponent),
      },
      {
        path: 'finanzen',
        title: 'Finanzen · souffleur',
        loadComponent: () =>
          import('./features/finance/journal.component').then((m) => m.JournalComponent),
      },
      {
        path: 'finanzen/belege',
        title: 'Belege · souffleur',
        loadComponent: () =>
          import('./features/finance/vouchers.component').then((m) => m.VouchersComponent),
      },
      {
        path: 'finanzen/bank',
        title: 'Bank · souffleur',
        loadComponent: () =>
          import('./features/finance/bank.component').then((m) => m.BankComponent),
      },
      {
        path: 'finanzen/budget',
        component: PlaceholderComponent,
        data: {
          title: 'Budget',
          note: 'Jahresetat auf Fixkosten und Produktionen verteilen, Plan gegen Ist aus der Buchhaltung.',
        },
      },
      {
        path: 'finanzen/inventar',
        component: PlaceholderComponent,
        data: {
          title: 'Inventar',
          note: 'Gegenstände mit Ort, Zustand und Anschaffungsbeleg. Gehört zu den Finanzen, weil jeder Gegenstand an einem Beleg hängt.',
        },
      },
      {
        path: 'kalender',
        title: 'Kalender · souffleur',
        loadComponent: () =>
          import('./features/calendar/calendar.component').then((m) => m.CalendarComponent),
      },
      {
        path: 'einstellungen/kalender-abos',
        title: 'Kalender abonnieren · souffleur',
        loadComponent: () =>
          import('./features/settings/subscriptions.component').then(
            (m) => m.SubscriptionsComponent,
          ),
      },
      {
        path: 'einstellungen/haus',
        title: 'Haus · souffleur',
        loadComponent: () =>
          import('./features/settings/haus.component').then((m) => m.HausComponent),
      },
      {
        path: 'einstellungen/module',
        title: 'Module des Hauses · souffleur',
        loadComponent: () =>
          import('./features/settings/modules.component').then((m) => m.ModulesComponent),
      },
      {
        path: 'dokumente',
        title: 'Dokumente · souffleur',
        loadComponent: () =>
          import('./features/documents/dokumente.component').then((m) => m.DokumenteComponent),
      },
      {
        path: 'einstellungen/benachrichtigungen',
        title: 'Benachrichtigungen · souffleur',
        loadComponent: () =>
          import('./features/settings/benachrichtigungen.component').then(
            (m) => m.BenachrichtigungenComponent,
          ),
      },
      {
        path: 'einstellungen/zugaenge',
        title: 'Zugänge · souffleur',
        loadComponent: () =>
          import('./features/settings/zugaenge.component').then((m) => m.ZugaengeComponent),
      },
      {
        path: 'einstellungen/dienstbedarf',
        title: 'Bedarf je Terminart · souffleur',
        loadComponent: () =>
          import('./features/settings/dienstbedarf.component').then((m) => m.DienstbedarfComponent),
      },
      {
        path: 'einstellungen/freie-tage',
        title: 'Freie Tage · souffleur',
        loadComponent: () =>
          import('./features/settings/freie-tage.component').then((m) => m.FreieTageComponent),
      },
      {
        path: 'einstellungen/ordner',
        title: 'Ordner der Ablage · souffleur',
        loadComponent: () =>
          import('./features/settings/ordner.component').then((m) => m.OrdnerComponent),
      },
      {
        path: 'einstellungen/kontenplan',
        title: 'Kontenplan · souffleur',
        loadComponent: () =>
          import('./features/settings/kontenplan.component').then((m) => m.KontenplanComponent),
      },
      {
        path: 'einstellungen/ressourcen',
        title: 'Ressourcen des Hauses · souffleur',
        loadComponent: () =>
          import('./features/settings/resources.component').then((m) => m.ResourcesComponent),
      },
      {
        path: 'einstellungen/listen/:catalog',
        title: 'Wertelisten · souffleur',
        loadComponent: () =>
          import('./features/settings/catalogs.component').then((m) => m.CatalogsComponent),
      },
      {
        path: 'einstellungen/listen',
        pathMatch: 'full',
        redirectTo: 'einstellungen/listen/EventCategory',
      },
      {
        path: 'kontakte',
        title: 'Kontakte · souffleur',
        loadComponent: () =>
          import('./features/contacts/contacts.component').then((m) => m.ContactsComponent),
      },
      {
        path: 'personal',
        title: 'Personal · souffleur',
        loadComponent: () =>
          import('./features/personnel/dienste.component').then((m) => m.DiensteComponent),
      },
      {
        path: 'ticketing',
        title: 'Ticketing · souffleur',
        loadComponent: () =>
          import('./features/ticketing/ticketing.component').then((m) => m.TicketingComponent),
      },
      {
        path: 'touring',
        title: 'Touring · souffleur',
        loadComponent: () =>
          import('./features/touring/touring.component').then((m) => m.TouringComponent),
      },
      {
        path: 'produktionen',
        title: 'Produktionen · souffleur',
        loadComponent: () =>
          import('./features/produktionen/produktionen.component').then(
            (m) => m.ProduktionenComponent,
          ),
      },
      {
        path: 'aufgaben',
        title: 'Aufgaben · souffleur',
        loadComponent: () =>
          import('./features/tasks/tasks.component').then((m) => m.TasksComponent),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
