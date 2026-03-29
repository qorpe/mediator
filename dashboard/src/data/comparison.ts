export type ComparisonValue = "yes" | "no" | "partial" | string;

export interface ComparisonRow {
  key: string;
  qorpe: ComparisonValue;
  mediatr: ComparisonValue;
  qorpeHighlight: boolean;
}

export const comparisonData: ComparisonRow[] = [
  {
    key: "license",
    qorpe: "mitLicense",
    mediatr: "apacheLicense",
    qorpeHighlight: false,
  },
  {
    key: "cqrsTypes",
    qorpe: "yes",
    mediatr: "no",
    qorpeHighlight: true,
  },
  {
    key: "resultPattern",
    qorpe: "yes",
    mediatr: "no",
    qorpeHighlight: true,
  },
  {
    key: "pipelineBehaviors",
    qorpe: "eleven",
    mediatr: "custom",
    qorpeHighlight: true,
  },
  {
    key: "httpEndpoints",
    qorpe: "yes",
    mediatr: "no",
    qorpeHighlight: true,
  },
  {
    key: "streaming",
    qorpe: "yes",
    mediatr: "partial",
    qorpeHighlight: true,
  },
  {
    key: "performance",
    qorpe: "faster",
    mediatr: "baseline",
    qorpeHighlight: true,
  },
  {
    key: "domainEvents",
    qorpe: "builtIn",
    mediatr: "notification",
    qorpeHighlight: true,
  },
  {
    key: "fluentValidation",
    qorpe: "native",
    mediatr: "thirdParty",
    qorpeHighlight: true,
  },
];
