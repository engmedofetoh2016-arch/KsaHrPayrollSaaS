export interface LeaveRequest {
  id: string;
  employeeId: string;
  employeeName: string;
  leaveType: number;
  startDate: string;
  endDate: string;
  totalDays: number;
  reason: string;
  status: number;
  rejectionReason?: string;
  reviewedByUserId?: string;
  reviewedAtUtc?: string;
  createdAtUtc: string;
}

export interface LeaveAttachment {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  createdAtUtc: string;
  employeeId: string;
}

export interface CreateLeaveRequestPayload {
  employeeId?: string;
  leaveType: number;
  startDate: string;
  endDate: string;
  reason: string;
}

export interface LeaveBalancePreviewRequest {
  employeeId?: string;
  leaveType: number;
  startDate: string;
  endDate: string;
}

export interface LeaveBalancePreviewResult {
  employeeId: string;
  leaveType: number;
  startDate: string;
  endDate: string;
  requestedDays: number;
  allocatedDays: number;
  usedDays: number;
  remainingBefore: number;
  remainingAfter: number;
  canSubmit: boolean;
  message: string;
}

export interface LeaveBalance {
  id: string;
  employeeId: string;
  employeeName: string;
  year: number;
  leaveType: number;
  allocatedDays: number;
  usedDays: number;
  remainingDays: number;
}

export interface UpsertLeaveBalanceRequest {
  employeeId: string;
  year: number;
  leaveType: number;
  allocatedDays: number;
  usedDays: number;
}
