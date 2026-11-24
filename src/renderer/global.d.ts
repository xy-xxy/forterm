import type { FortermAPI } from "../shared/types";
declare global {
  interface Window {
    forterm: FortermAPI;
  }
}
