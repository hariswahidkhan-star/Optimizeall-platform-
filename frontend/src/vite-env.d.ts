/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** 'true' exposes /design-system in non-development builds (staging). */
  readonly VITE_SHOW_DESIGN_SYSTEM?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
