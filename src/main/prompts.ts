import { randomUUID } from "node:crypto";
import type { Prompt, AppEvent } from "../shared/types";
export class Prompts {
  private pending = new Map<
    string,
    {
      prompt: Prompt;
      resolve: (v: string[] | null) => void;
      timer: NodeJS.Timeout;
    }
  >();
  constructor(private emit: (e: AppEvent) => void) {}
  ask(input: Omit<Prompt, "id">): Promise<string[] | null> {
    return new Promise((resolve) => {
      const prompt = { ...input, id: randomUUID() };
      const timer = setTimeout(() => this.answer(prompt.id, null), 120000);
      this.pending.set(prompt.id, { prompt, resolve, timer });
      this.emit({ type: "prompt", prompt });
    });
  }
  list() {
    return [...this.pending.values()].map((x) => x.prompt);
  }
  answer(id: string, answers: string[] | null) {
    const p = this.pending.get(id);
    if (!p) return;
    clearTimeout(p.timer);
    this.pending.delete(id);
    p.resolve(answers);
    this.emit({ type: "prompt-closed", id });
  }
  close() {
    for (const id of this.pending.keys()) this.answer(id, null);
  }
}
