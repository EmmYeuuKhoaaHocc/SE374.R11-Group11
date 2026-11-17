import {
  registerResources,
  type LocalizedResources,
  type ResourceRegistration,
} from "@neuroscan/i18n";

interface ResourceModule {
  default: LocalizedResources;
}

const modules = import.meta.glob<ResourceModule>(
  ["./resource.ts", "../features/*/resource.ts"],
  { eager: true },
);

export const registeredResourceSources = Object.keys(modules).sort();

export const resourceRegistrations = Object.entries(modules).map(
  ([source, module]) => ({
    source: `portal:${source}`,
    resources: module.default,
  }),
) satisfies ResourceRegistration[];

registerResources(resourceRegistrations);
