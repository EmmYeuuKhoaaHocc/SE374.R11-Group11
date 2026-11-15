export interface TablePaginationParams {
  pageNumber: number;
  pageSize: number;
}

export interface CursorPaginationParams {
  pageSize: number;
  cursor: string | null;
}

export type InitialCursorPaginationParams = Omit<CursorPaginationParams, "cursor">;

export interface TablePaginationMetadata {
  page: number;
  pageSize: number;
  totalRecords: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

export interface CursorPaginationMetadata {
  pageSize: number;
  nextCursor: string | null;
  hasNextPage: boolean;
}

export interface PaginatedData<T, TMetadata> {
  items: T[];
  metadata: TMetadata;
}

export type TablePaginatedData<T> = PaginatedData<T, TablePaginationMetadata>;

export type CursorPaginatedData<T> = PaginatedData<T, CursorPaginationMetadata>;
