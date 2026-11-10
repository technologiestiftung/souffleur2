/** Die Antworten der Schnittstelle, so wie sie hereinkommen. */

export interface JournalLine {
  id: string;
  accountId: string;
  debit: number;
  credit: number;
  taxKey: string | null;
  taxAmount: number;
  taxSphere: number | null;
  costCenterId: string | null;
  contactId: string | null;
  description: string | null;
}

export interface JournalEntry {
  id: string;
  number: number;
  fiscalYear: number;
  date: string;
  description: string;
  documentNumber: string | null;
  isCommitted: boolean;
  committedAt: string | null;
  reversalOfId: string | null;
  total: number;
  lines: JournalLine[];
}

export interface Account {
  id: string;
  number: string;
  name: string;
  type: number;
  taxKey: string | null;
  defaultTaxSphere: number | null;
  isActive: boolean;
}

export interface CostCenter {
  id: string;
  number: string;
  name: string;
}

export interface OrganizationInfo {
  name: string;
  chartOfAccounts: number;
  chartLabel: string;
  isNonProfit: boolean;
  requiresTaxSpheres: boolean;
}
