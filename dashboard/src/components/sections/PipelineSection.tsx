import { useTranslation } from "react-i18next";
import { motion } from "framer-motion";
import {
  ScrollText,
  FileText,
  ShieldAlert,
  ShieldCheck,
  CheckSquare,
  Fingerprint,
  Database,
  Gauge,
  RefreshCw,
  HardDrive,
  Trash2,
} from "lucide-react";
import { SectionHeading } from "@/components/ui/SectionHeading";
import { cn } from "@/lib/utils";

const behaviors = [
  { key: "audit", icon: ScrollText, order: 1 },
  { key: "logging", icon: FileText, order: 2 },
  { key: "exception", icon: ShieldAlert, order: 3 },
  { key: "authorization", icon: ShieldCheck, order: 4 },
  { key: "validation", icon: CheckSquare, order: 5 },
  { key: "idempotency", icon: Fingerprint, order: 6 },
  { key: "transaction", icon: Database, order: 7 },
  { key: "performanceMon", icon: Gauge, order: 8 },
  { key: "retry", icon: RefreshCw, order: 9 },
  { key: "caching", icon: HardDrive, order: 10 },
  { key: "cacheInvalidation", icon: Trash2, order: 11 },
];

export function PipelineSection() {
  const { t } = useTranslation();

  return (
    <section id="pipeline" className="py-24 sm:py-32">
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        <SectionHeading
          title={t("pipeline.title")}
          subtitle={t("pipeline.subtitle")}
        />

        {/* Pipeline Flow */}
        <div className="relative max-w-4xl mx-auto">
          {/* Center line */}
          <div className="absolute start-6 sm:start-1/2 top-0 bottom-0 w-px bg-gradient-to-b from-brand-500/0 via-brand-500/30 to-brand-500/0 sm:-translate-x-px" />

          <div className="space-y-4">
            {behaviors.map((behavior, i) => {
              const isLeft = i % 2 === 0;
              return (
                <motion.div
                  key={behavior.key}
                  initial={{ opacity: 0, x: isLeft ? -20 : 20 }}
                  whileInView={{ opacity: 1, x: 0 }}
                  viewport={{ once: true, margin: "-30px" }}
                  transition={{ duration: 0.35, delay: i * 0.05 }}
                  className={cn(
                    "relative flex items-center gap-4",
                    "ps-14 sm:ps-0",
                    isLeft
                      ? "sm:flex-row sm:pe-[calc(50%+2rem)]"
                      : "sm:flex-row-reverse sm:ps-[calc(50%+2rem)]",
                  )}
                >
                  {/* Connector dot */}
                  <div className="absolute start-4 sm:start-1/2 w-4 h-4 rounded-full border-2 border-brand-500 bg-surface sm:-translate-x-2 z-10" />

                  {/* Card */}
                  <div className="flex-1 rounded-xl border border-border bg-surface-alt p-4 hover:shadow-md hover:border-brand-200 dark:hover:border-brand-800 transition-all duration-300 group">
                    <div className="flex items-start gap-3">
                      <div className="p-2 rounded-lg bg-brand-500/10 shrink-0 group-hover:bg-brand-500/20 transition-colors">
                        <behavior.icon className="w-4 h-4 text-brand-600 dark:text-brand-400" />
                      </div>
                      <div className="min-w-0">
                        <div className="flex items-center gap-2 mb-1">
                          <span className="text-xs font-mono text-brand-600 dark:text-brand-400 bg-brand-500/10 px-1.5 py-0.5 rounded">
                            {behavior.order}
                          </span>
                          <h3 className="text-sm font-semibold text-text-primary truncate">
                            {t(`pipeline.behaviors.${behavior.key}.title`)}
                          </h3>
                        </div>
                        <p className="text-xs text-text-secondary leading-relaxed">
                          {t(`pipeline.behaviors.${behavior.key}.description`)}
                        </p>
                      </div>
                    </div>
                  </div>
                </motion.div>
              );
            })}
          </div>
        </div>
      </div>
    </section>
  );
}
