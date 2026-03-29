import { useEffect } from "react";
import { useTranslation } from "react-i18next";
import { languages } from "@/i18n";

export function useDirection() {
  const { i18n } = useTranslation();

  useEffect(() => {
    const lang = languages.find((l) => l.code === i18n.language);
    const dir = lang?.dir ?? "ltr";
    document.documentElement.dir = dir;
    document.documentElement.lang = i18n.language;
  }, [i18n.language]);
}
