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
