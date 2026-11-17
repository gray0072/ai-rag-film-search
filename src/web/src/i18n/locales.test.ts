import { describe, expect, it } from "vitest";
import en from "./locales/en.json";
import ru from "./locales/ru.json";
import sv from "./locales/sv.json";

function keysOf(value: unknown, prefix = ""): string[] {
  if (typeof value !== "object" || value === null) {
    return [prefix];
  }
  return Object.entries(value).flatMap(([key, child]) =>
    keysOf(child, prefix ? `${prefix}.${key}` : key),
  );
}

describe("locale resources", () => {
  const english = keysOf(en).sort();

  it.each([
    ["ru", ru],
    ["sv", sv],
  ])("%s has the same keys as en", (_, resources) => {
    expect(keysOf(resources).sort()).toEqual(english);
  });
});
