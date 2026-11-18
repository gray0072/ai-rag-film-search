import "@testing-library/jest-dom/vitest";
import "@/i18n";
import { cleanup } from "@testing-library/react";
import { afterEach } from "vitest";

// jsdom has no ResizeObserver; Radix primitives (checkbox) measure themselves with it.
class ResizeObserverStub {
  observe() {
    // Nothing to measure in jsdom.
  }
  unobserve() {
    // Nothing to measure in jsdom.
  }
  disconnect() {
    // Nothing to measure in jsdom.
  }
}
if (!("ResizeObserver" in globalThis)) {
  globalThis.ResizeObserver = ResizeObserverStub;
}

afterEach(() => {
  cleanup();
});
