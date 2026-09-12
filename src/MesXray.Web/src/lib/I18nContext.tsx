import { createContext, useContext, useMemo, type ReactNode } from "react";
import type { Layer, NodeStatus, NodeType } from "../api/types";
import { layerLabel, relationLabel, statusLabel, statusShort, translate, typeLabel, type Lang, type MessageKey, type Vars } from "./i18n";

export interface I18n {
  lang: Lang;
  t: (key: MessageKey, vars?: Vars) => string;
  type: (type: NodeType) => string;
  status: (status: NodeStatus) => string;
  statusShort: (status: NodeStatus) => string;
  layer: (layer: Layer) => string;
  relation: (relation: string | null | undefined) => string;
}

const build = (lang: Lang): I18n => ({
  lang,
  t: (key, vars) => translate(lang, key, vars),
  type: (type) => typeLabel(lang, type),
  status: (status) => statusLabel(lang, status),
  statusShort: (status) => statusShort(lang, status),
  layer: (layer) => layerLabel(lang, layer),
  relation: (relation) => relationLabel(lang, relation),
});

const I18nContext = createContext<I18n>(build("en"));

export function I18nProvider({ lang, children }: { lang: Lang; children: ReactNode }) {
  const value = useMemo(() => build(lang), [lang]);
  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>;
}

export const useI18n = (): I18n => useContext(I18nContext);
