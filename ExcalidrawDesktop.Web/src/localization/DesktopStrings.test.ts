import { describe, expect, it } from "vitest";

import {
  getDesktopErrorString,
  getDesktopString,
  isSupportedLanguageCode,
  supportedLanguageCodes,
} from "./DesktopStrings";

describe("desktop localization", () => {
  it("contains the planned native-to-Excalidraw language mappings", () => {
    expect(supportedLanguageCodes).toEqual([
      "en",
      "es-ES",
      "fr-FR",
      "de-DE",
      "pt-BR",
      "ja-JP",
      "zh-CN",
      "ar-SA",
    ]);
  });

  it("returns translated strings and falls back to English", () => {
    expect(getDesktopString("de-DE", "save")).toBe("Speichern");
    expect(getDesktopString("unsupported", "save")).toBe("Save");
  });

  it("maps stable bridge error codes to localized presentation text", () => {
    expect(
      getDesktopErrorString("ja-JP", "DocumentNotFound", "openFailed"),
    ).toContain("存在しません");
    expect(getDesktopErrorString("es-ES", "Unknown", "saveFailed")).toBe(
      "No se pudo guardar el dibujo.",
    );
  });

  it("rejects unsupported language codes", () => {
    expect(isSupportedLanguageCode("ar-SA")).toBe(true);
    expect(isSupportedLanguageCode("../../bad")).toBe(false);
  });

  it("keeps export failure messages distinct in every supported language", () => {
    for (const language of supportedLanguageCodes) {
      const messages = ["ExportEmpty", "ExportDimensionLimit", "ExportByteLimit", "ExportInvalidImage", "ExportUploadFailed"]
        .map((code) => getDesktopErrorString(language, code, "exportFailed"));
      expect(messages.every(Boolean)).toBe(true);
      expect(new Set(messages).size).toBe(messages.length);
    }
  });

  it("provides localized retry, library, and recovery guidance", () => {
    for (const language of supportedLanguageCodes) {
      expect(getDesktopString(language, "settingsRetry")).toBeTruthy();
      expect(getDesktopString(language, "libraryUnavailable")).toContain(language === "en" ? "Library" : "");
      expect(getDesktopString(language, "recoveryUnavailable")).toBeTruthy();
    }
    expect(getDesktopErrorString("en", "ExportEmpty", "exportFailed")).toContain("Add something");
    expect(getDesktopErrorString("en", "ExportByteLimit", "exportFailed")).toContain("100 MB");
  });
});
