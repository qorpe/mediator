import { useTranslation } from "react-i18next";
import { motion } from "framer-motion";
import {
  GitBranch,
  CheckCircle,
  Workflow,
  Gauge,
  Globe,
  Radio,
} from "lucide-react";
import { Card } from "@/components/ui/Card";
import { SectionHeading } from "@/components/ui/SectionHeading";

const features = [
  { key: "cqrs", icon: GitBranch },
  { key: "result", icon: CheckCircle },
  { key: "pipeline", icon: Workflow },
  { key: "performance", icon: Gauge },
  { key: "aspnetcore", icon: Globe },
  { key: "streaming", icon: Radio },
];

export function FeaturesSection() {
  const { t } = useTranslation();

  return (
    <section id="features" className="py-24 sm:py-32">
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        <SectionHeading
          title={t("features.title")}
          subtitle={t("features.subtitle")}
        />

        <div className="grid sm:grid-cols-2 lg:grid-cols-3 gap-6">
          {features.map((feature, i) => (
            <motion.div
              key={feature.key}
              initial={{ opacity: 0, y: 20 }}
              whileInView={{ opacity: 1, y: 0 }}
              viewport={{ once: true, margin: "-50px" }}
              transition={{ duration: 0.4, delay: i * 0.08 }}
            >
              <Card hoverable className="h-full">
                <div className="p-2 w-fit rounded-xl bg-brand-500/10 mb-4">
                  <feature.icon className="w-6 h-6 text-brand-600 dark:text-brand-400" />
                </div>
                <h3 className="text-lg font-semibold text-text-primary mb-2">
                  {t(`features.${feature.key}.title`)}
                </h3>
                <p className="text-sm text-text-secondary leading-relaxed">
                  {t(`features.${feature.key}.description`)}
                </p>
              </Card>
            </motion.div>
          ))}
        </div>
      </div>
    </section>
  );
}
