import "i18next";
import type en from "./locales/en.json";

// Typed translation keys: `t("nav.cart")` is checked at compile time against the English resources.
declare module "i18next" {
  interface CustomTypeOptions {
    defaultNS: "translation";
    resources: {
      translation: typeof en;
    };
  }
}
