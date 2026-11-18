import type { TFunction } from "i18next";
import { problemCode } from "@/api/problem";

/** Commerce error codes of the API (spec 04, `CommerceError`) and their messages. */
const commerceErrorKeys = {
  movieNotFound: "errors.commerce.movieNotFound",
  offerNotAvailable: "errors.commerce.offerNotAvailable",
  alreadyOwned: "errors.commerce.alreadyOwned",
  alreadyRented: "errors.commerce.alreadyRented",
  cartEmpty: "errors.commerce.cartEmpty",
  cartHasUnavailableItems: "errors.commerce.cartHasUnavailableItems",
  orderNotFound: "errors.commerce.orderNotFound",
  orderNotPayable: "errors.commerce.orderNotPayable",
} as const;

type CommerceErrorCode = keyof typeof commerceErrorKeys;

function isCommerceErrorCode(code: string | undefined): code is CommerceErrorCode {
  return code !== undefined && Object.hasOwn(commerceErrorKeys, code);
}

/** A user-facing message for a failed commerce call: by error code, never from the API's English text. */
export function commerceErrorMessage(t: TFunction, error: unknown): string {
  const code = problemCode(error);
  return isCommerceErrorCode(code) ? t(commerceErrorKeys[code]) : t("common.errorGeneric");
}
