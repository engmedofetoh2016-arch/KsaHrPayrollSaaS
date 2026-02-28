export interface AttendanceInputRow {
  id: string;
  employeeId: string;
  employeeName: string;
  year: number;
  month: number;
  daysPresent: number;
  daysAbsent: number;
  overtimeHours: number;
}

export interface UpsertAttendanceInputRequest {
  employeeId: string;
  year: number;
  month: number;
  daysPresent: number;
  daysAbsent: number;
  overtimeHours: number;
}

export interface MyBookingRow {
  id: string;
  workDate: string;
  hoursWorked: number;
  status: string;
  isWeekend: boolean;
  isHoliday: boolean;
  checkInUtc?: string | null;
  checkOutUtc?: string | null;
}

export interface TimesheetBookingRow {
  id: string;
  employeeId: string;
  employeeName: string;
  workDate: string;
  hoursWorked: number;
  approvedOvertimeHours: number;
  isWeekend: boolean;
  isHoliday: boolean;
  status: string;
  shiftRuleId?: string | null;
  approvedAtUtc?: string | null;
  checkInUtc?: string | null;
  checkOutUtc?: string | null;
}

export interface ManualBookingRequest {
  workDate: string;
  checkInUtc?: string | null;
  checkOutUtc?: string | null;
  hoursWorked?: number | null;
  notes?: string | null;
}
