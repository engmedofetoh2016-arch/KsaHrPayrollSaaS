export interface OffboardingRecord {
  id: string;
  employeeId: string;
  employeeName: string;
  effectiveDate: string;
  reason: string;
  status: string;
  approvedAtUtc?: string | null;
  paidAtUtc?: string | null;
  closedAtUtc?: string | null;
  createdAtUtc: string;
}

export interface CreateOffboardingRequest {
  employeeId: string;
  effectiveDate: string;
  reason: string;
}

export interface OffboardingChecklist {
  id: string;
  offboardingId: string;
  employeeId: string;
  status: string;
  completedAtUtc?: string | null;
  notes: string;
  totalItems: number;
  completedItems: number;
  completionPercent: number;
  items: OffboardingChecklistItem[];
}

export interface OffboardingChecklistItem {
  id: string;
  itemCode: string;
  itemLabel: string;
  status: string;
  notes: string;
  sortOrder: number;
  completedAtUtc?: string | null;
  completedByUserId?: string | null;
}

export interface CreateOffboardingChecklistItemRequest {
  itemCode: string;
  itemLabel: string;
  sortOrder: number;
  notes?: string | null;
}
