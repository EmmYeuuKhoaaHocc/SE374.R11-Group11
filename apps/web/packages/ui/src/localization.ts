import { createContext, createElement, useContext, useMemo, type ReactNode } from "react";

export interface UiPaginationMessages {
  previous: string;
  next: string;
  first: string;
  last: string;
  navigation: string;
  page: (page: number) => string;
}

export interface UiDataTableMessages {
  caption?: string;
  sortBy: (header: string) => string;
  sortStatus?: (header: string, direction: "ASC" | "DESC") => string;
  loading: string;
  refreshing: string;
  errorHeading: string;
  retry: string;
  rowsPerPage: string;
  total: (count: number) => string;
  emptyMessage: string;
  emptyDescription: string;
}

export interface UiMessages {
  spinner: {
    loading: string;
  };
  pagination: UiPaginationMessages;
  dataTable: UiDataTableMessages;
  dialog: {
    close: string;
  };
  confirmDialog: {
    confirm: string;
    cancel: string;
    error: string;
  };
  breadcrumb: {
    navigation: string;
  };
  formErrorSummary: {
    heading: string;
  };
  fileUpload: {
    browse: string;
    remove: (name: string) => string;
    selectedFiles: (count: number) => string;
    invalidType: string;
    fileTooLarge: (name: string) => string;
    tooManyFiles: (count: number) => string;
  };
  searchInput: {
    clear: string;
  };
  passwordInput: {
    show: string;
    hide: string;
  };
  datePicker: {
    clear: string;
  };
  dateRangePicker: {
    startLabel: string;
    endLabel: string;
    clear: string;
  };
  timePicker: {
    clear: string;
  };
  combobox: {
    noOptions: string;
    clear?: string;
    loading?: string;
    loadMore?: string;
  };
  multiSelect: {
    noOptions: string;
    remove: (label: string) => string;
    selectedCount: (count: number) => string;
    clear?: string;
    loading?: string;
    loadMore?: string;
    selectAll?: string;
    clearAll?: string;
  };
  drawer: {
    close: string;
  };
  dropdownMenu: {
    menu: string;
  };
  stepper: {
    progress: string;
    completed: string;
    current: string;
  };
}

export interface UiFormatters {
  number: (value: number, options?: Intl.NumberFormatOptions) => string;
  date: (value: Date | number | string, options?: Intl.DateTimeFormatOptions) => string;
  currency: (
    value: number,
    currency: string,
    options?: Omit<Intl.NumberFormatOptions, "currency" | "style">,
  ) => string;
}

export type UiMessagesOverrides = {
  [K in keyof UiMessages]?: Partial<UiMessages[K]>;
};

export interface UiLocalizationConfig {
  locale?: string;
  messages?: UiMessagesOverrides;
  formatters?: Partial<UiFormatters>;
}

export interface UiLocalizationValue {
  locale: string;
  messages: UiMessages;
  formatters: UiFormatters;
}

const FALLBACK_LOCALE = "en-US";

export const DEFAULT_UI_MESSAGES: UiMessages = {
  spinner: {
    loading: "Loading",
  },
  pagination: {
    previous: "Previous",
    next: "Next",
    first: "First",
    last: "Last",
    navigation: "Pagination",
    page: (page) => `Go to page ${page}`,
  },
  dataTable: {
    caption: "Data table",
    sortBy: (header) => `Sort by ${header}`,
    sortStatus: (header, direction) => `Sorted by ${header}, ${direction === "ASC" ? "ascending" : "descending"}`,
    loading: "Loading",
    refreshing: "Refreshing",
    errorHeading: "Unable to load data",
    retry: "Try again",
    rowsPerPage: "Rows per page",
    total: (count) => `${count} total`,
    emptyMessage: "No records found",
    emptyDescription: "Try changing the filters or check back later.",
  },
  dialog: {
    close: "Close",
  },
  confirmDialog: {
    confirm: "Confirm",
    cancel: "Cancel",
    error: "The action could not be completed. Please try again.",
  },
  breadcrumb: {
    navigation: "Breadcrumb",
  },
  formErrorSummary: {
    heading: "Please correct the following errors.",
  },
  fileUpload: {
    browse: "Choose files",
    remove: (name) => `Remove ${name}`,
    selectedFiles: (count) => `${count} file${count === 1 ? "" : "s"} selected`,
    invalidType: "One or more files have an unsupported type.",
    fileTooLarge: (name) => `${name} is too large.`,
    tooManyFiles: (count) => `You can select up to ${count} files.`,
  },
  searchInput: {
    clear: "Clear search",
  },
  passwordInput: {
    show: "Show password",
    hide: "Hide password",
  },
  datePicker: {
    clear: "Clear date",
  },
  dateRangePicker: {
    startLabel: "Start date",
    endLabel: "End date",
    clear: "Clear date range",
  },
  timePicker: {
    clear: "Clear time",
  },
  combobox: {
    noOptions: "No options found",
    clear: "Clear selection",
    loading: "Loading options",
    loadMore: "Load more options",
  },
  multiSelect: {
    noOptions: "No options found",
    remove: (label) => `Remove ${label}`,
    selectedCount: (count) => `${count} selected`,
    clear: "Clear selections",
    loading: "Loading options",
    loadMore: "Load more options",
    selectAll: "Select all",
    clearAll: "Clear all",
  },
  drawer: {
    close: "Close",
  },
  dropdownMenu: {
    menu: "Menu",
  },
  stepper: {
    progress: "Progress",
    completed: "Completed",
    current: "Current step",
  },
};

function toDate(value: Date | number | string): Date {
  return value instanceof Date ? value : new Date(value);
}

function createDefaultFormatters(locale: string): UiFormatters {
  return {
    number: (value, options) => new Intl.NumberFormat(locale, options).format(value),
    date: (value, options) => new Intl.DateTimeFormat(locale, options).format(toDate(value)),
    currency: (value, currency, options) => new Intl.NumberFormat(locale, {
      ...options,
      currency,
      style: "currency",
    }).format(value),
  };
}

function mergeMessages(
  base: UiMessages,
  overrides: UiMessagesOverrides | undefined,
): UiMessages {
  if (!overrides) return base;

  return {
    ...base,
    spinner: { ...base.spinner, ...overrides.spinner },
    pagination: { ...base.pagination, ...overrides.pagination },
    dataTable: { ...base.dataTable, ...overrides.dataTable },
    dialog: { ...base.dialog, ...overrides.dialog },
    confirmDialog: { ...base.confirmDialog, ...overrides.confirmDialog },
    breadcrumb: { ...base.breadcrumb, ...overrides.breadcrumb },
    formErrorSummary: { ...base.formErrorSummary, ...overrides.formErrorSummary },
    fileUpload: { ...base.fileUpload, ...overrides.fileUpload },
    searchInput: { ...base.searchInput, ...overrides.searchInput },
    passwordInput: { ...base.passwordInput, ...overrides.passwordInput },
    datePicker: { ...base.datePicker, ...overrides.datePicker },
    dateRangePicker: { ...base.dateRangePicker, ...overrides.dateRangePicker },
    timePicker: { ...base.timePicker, ...overrides.timePicker },
    combobox: { ...base.combobox, ...overrides.combobox },
    multiSelect: { ...base.multiSelect, ...overrides.multiSelect },
    drawer: { ...base.drawer, ...overrides.drawer },
    dropdownMenu: { ...base.dropdownMenu, ...overrides.dropdownMenu },
    stepper: { ...base.stepper, ...overrides.stepper },
  };
}

const defaultValue: UiLocalizationValue = {
  locale: FALLBACK_LOCALE,
  messages: DEFAULT_UI_MESSAGES,
  formatters: createDefaultFormatters(FALLBACK_LOCALE),
};

const UiLocalizationContext = createContext<UiLocalizationValue>(defaultValue);

export interface UiLocalizationProviderProps {
  config?: UiLocalizationConfig;
  children: ReactNode;
}

export function UiLocalizationProvider({ config, children }: UiLocalizationProviderProps) {
  const parent = useContext(UiLocalizationContext);
  const locale = config?.locale ?? parent.locale;
  const value = useMemo<UiLocalizationValue>(() => ({
    locale,
    messages: mergeMessages(parent.messages, config?.messages),
    formatters: {
      ...parent.formatters,
      ...createDefaultFormatters(locale),
      ...config?.formatters,
    },
  }), [config, locale, parent]);

  return createElement(UiLocalizationContext.Provider, { value }, children);
}

export function useUiLocalization(): UiLocalizationValue {
  return useContext(UiLocalizationContext);
}
