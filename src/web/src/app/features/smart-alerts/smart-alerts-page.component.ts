import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { catchError, of } from 'rxjs';
import { SmartAlertItem } from '../../core/models/smart-alert.models';
import { SmartAlertsService } from '../../core/services/smart-alerts.service';

@Component({
  selector: 'app-smart-alerts-page',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './smart-alerts-page.component.html',
  styleUrl: './smart-alerts-page.component.scss'
})
export class SmartAlertsPageComponent implements OnInit {
  private readonly smartAlerts = inject(SmartAlertsService);

  readonly loading = signal(false);
  readonly error = signal('');
  readonly message = signal('');
  readonly actionKey = signal('');
  readonly items = signal<SmartAlertItem[]>([]);

  daysAhead = 30;

  readonly criticalCount = computed(() => this.items().filter((x) => x.severity === 'Critical').length);
  readonly warningCount = computed(() => this.items().filter((x) => x.severity === 'Warning').length);
  readonly noticeCount = computed(() => this.items().filter((x) => x.severity === 'Notice').length);

  ngOnInit(): void {
    this.load();
  }

  load() {
    this.loading.set(true);
    this.error.set('');
    this.message.set('');

    this.smartAlerts
      .list(this.daysAhead)
      .pipe(catchError(() => of({ total: 0, items: [], daysAhead: this.daysAhead })))
      .subscribe({
        next: (response) => {
          if (!response) {
            this.error.set('Failed to load smart alerts.');
            return;
          }
          this.items.set(Array.isArray(response.items) ? response.items : []);
        },
        complete: () => this.loading.set(false)
      });
  }

  acknowledge(key: string) {
    if (!key || this.actionKey()) {
      return;
    }

    this.actionKey.set(key);
    this.error.set('');
    this.message.set('');
    this.smartAlerts.acknowledge(key, { note: 'AcknowledgedFromUi' }).subscribe({
      next: () => {
        this.message.set('Alert acknowledged.');
        this.load();
      },
      error: () => this.error.set('Failed to acknowledge alert.'),
      complete: () => this.actionKey.set('')
    });
  }

  snooze(key: string, days: number) {
    if (!key || this.actionKey()) {
      return;
    }

    this.actionKey.set(key);
    this.error.set('');
    this.message.set('');
    this.smartAlerts.snooze(key, { days, note: 'SnoozedFromUi' }).subscribe({
      next: () => {
        this.message.set(`Alert snoozed for ${days} days.`);
        this.load();
      },
      error: () => this.error.set('Failed to snooze alert.'),
      complete: () => this.actionKey.set('')
    });
  }

  severityClass(severity: string): string {
    const normalized = (severity || '').toLowerCase();
    if (normalized === 'critical') return 'critical';
    if (normalized === 'warning') return 'warning';
    return 'notice';
  }
}
