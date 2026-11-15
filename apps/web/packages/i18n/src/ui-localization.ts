import type { UiLocalizationConfig } from "@neuroscan/ui";

type TranslationFunction = (key: string, options?: Record<string, unknown>) => string;

export function createUiLocalizationConfig(
  t: TranslationFunction,
  locale: string,
): UiLocalizationConfig {
  return {
    locale,
    messages: {
      spinner: {
        loading: t("common.ui.spinner.loading"),
      },
      pagination: {
        previous: t("common.ui.pagination.previous"),
        next: t("common.ui.pagination.next"),
        first: t("common.ui.pagination.first"),
        last: t("common.ui.pagination.last"),
        navigation: t("common.ui.pagination.navigation"),
        page: (page) => t("common.ui.pagination.page", { page }),
      },
      dataTable: {
        caption: t("common.ui.dataTable.caption"),
        sortBy: (header) => t("common.ui.dataTable.sortBy", { header }),
        sortStatus: (header, direction) => t("common.ui.dataTable.sortStatus", {
          header,
          direction: t(`common.ui.dataTable.${direction === "ASC" ? "ascending" : "descending"}`),
        }),
        loading: t("common.ui.dataTable.loading"),
        refreshing: t("common.ui.dataTable.refreshing"),
        errorHeading: t("common.ui.dataTable.errorHeading"),
        retry: t("common.ui.dataTable.retry"),
        rowsPerPage: t("common.ui.dataTable.rowsPerPage"),
        total: (count) => t("common.ui.dataTable.total", { count }),
        emptyMessage: t("common.ui.dataTable.emptyMessage"),
        emptyDescription: t("common.ui.dataTable.emptyDescription"),
      },
      dialog: {
        close: t("common.ui.dialog.close"),
      },
      confirmDialog: {
        confirm: t("common.ui.confirmDialog.confirm"),
        cancel: t("common.ui.confirmDialog.cancel"),
        error: t("common.ui.confirmDialog.error"),
      },
      breadcrumb: {
        navigation: t("common.ui.breadcrumb.navigation"),
      },
      formErrorSummary: {
        heading: t("common.ui.formErrorSummary.heading"),
      },
      fileUpload: {
        browse: t("common.ui.fileUpload.browse"),
        remove: (name) => t("common.ui.fileUpload.remove", { name }),
        selectedFiles: (count) => t("common.ui.fileUpload.selectedFiles", { count }),
        invalidType: t("common.ui.fileUpload.invalidType"),
        fileTooLarge: (name) => t("common.ui.fileUpload.fileTooLarge", { name }),
        tooManyFiles: (count) => t("common.ui.fileUpload.tooManyFiles", { count }),
      },
      searchInput: { clear: t("common.ui.searchInput.clear") },
      passwordInput: {
        show: t("common.ui.passwordInput.show"),
        hide: t("common.ui.passwordInput.hide"),
      },
      datePicker: { clear: t("common.ui.datePicker.clear") },
      dateRangePicker: {
        startLabel: t("common.ui.dateRangePicker.startLabel"),
        endLabel: t("common.ui.dateRangePicker.endLabel"),
        clear: t("common.ui.dateRangePicker.clear"),
      },
      timePicker: { clear: t("common.ui.timePicker.clear") },
      combobox: { noOptions: t("common.ui.combobox.noOptions") },
      multiSelect: {
        noOptions: t("common.ui.multiSelect.noOptions"),
        remove: (label) => t("common.ui.multiSelect.remove", { label }),
        selectedCount: (count) => t("common.ui.multiSelect.selectedCount", { count }),
      },
      drawer: { close: t("common.ui.drawer.close") },
      dropdownMenu: { menu: t("common.ui.dropdownMenu.menu") },
      stepper: {
        progress: t("common.ui.stepper.progress"),
        completed: t("common.ui.stepper.completed"),
        current: t("common.ui.stepper.current"),
      },
    },
  };
}
