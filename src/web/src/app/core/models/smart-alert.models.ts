export type SmartAlertSeverity = 'Critical' | 'Warning' | 'Notice' | string;

export interface SmartAlertItem {
  key: string;
  type: string;
  severity: SmartAlertSeverity;
  subject?: string;
  message: string;
  daysLeft?: number;
  dueDate?: string;
}

export interface SmartAlertsResponse {
  daysAhead: number;
  total: number;
  items: SmartAlertItem[];
}

export interface SmartAlertActionRequest {
  note?: string;
}

export interface SmartAlertSnoozeRequest extends SmartAlertActionRequest {
  days: number;
}

export interface SmartAlertExplainResponse {
  key: string;
  provider: string;
  usedFallback: boolean;
  explanation: string;
  nextAction: string;
  targetWindowDays: number;
  generatedAtUtc: string;
}
