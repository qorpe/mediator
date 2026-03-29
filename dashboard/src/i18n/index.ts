import i18n from "i18next";
import { initReactI18next } from "react-i18next";
import LanguageDetector from "i18next-browser-languagedetector";

import en from "./locales/en.json";
import de from "./locales/de.json";
import tr from "./locales/tr.json";
import fr from "./locales/fr.json";
import es from "./locales/es.json";
import ar from "./locales/ar.json";

export const languages = [
  { code: "en", name: "English", dir: "ltr" as const },
  { code: "de", name: "Deutsch", dir: "ltr" as const },
  { code: "tr", name: "Turkce", dir: "ltr" as const },
  { code: "fr", name: "Francais", dir: "ltr" as const },
  { code: "es", name: "Espanol", dir: "ltr" as const },
  { code: "ar", name: "العربية", dir: "rtl" as const },
] as const;

i18n
  .use(LanguageDetector)
  .use(initReactI18next)
  .init({
    resources: {
      en: { translation: en },
      de: { translation: de },
      tr: { translation: tr },
      fr: { translation: fr },
      es: { translation: es },
      ar: { translation: ar },
    },
    fallbackLng: "en",
    interpolation: {
      escapeValue: false,
    },
    detection: {
      order: ["navigator", "localStorage", "htmlTag"],
      caches: ["localStorage"],
    },
  });

export default i18n;
